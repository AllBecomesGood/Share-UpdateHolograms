using System;
using System.Collections.Generic;
using UnityEngine;
using HoloToolkit.Unity;

namespace HoloToolkit.Sharing.Tests
{
    /// <summary>
    /// Synchronizes positions, rotations and scaling of objects assigned to the list called
    /// listOfGameObjToKeepSynced
    /// You must manually assign objects you want to keep synced to this list.
    /// You also have to set up the "old way" of sharing, using "SharingService.exe".
    /// Also broadcasts head transform of local user to other users in the session,
    /// and adds and updates the head transforms of remote users.
    /// Head transforms are sent and received in the local coordinate space of the GameObject this component is on.
    /// </summary>
    public class SyncManager : Singleton<SyncManager>
    {
        // List in which we have to manually (in Unity) put all objects that we want to keep synced.
        public List<GameObject> listOfGameObjToKeepSynced = new List<GameObject>();

        /// <summary>
        /// Debug Text to show info
        /// </summary>
        public TextMesh AnchorDebugText;

        public class RemoteHeadInfo
        {
            public long UserID;
            public GameObject HeadObject;
        }

        public class RemoteObjectInfoStruct
        {
            public int objNumber; // Will be same as index and also position in listOfGameObjToKeepSynced.
            public Vector3    oldPos;
            public Quaternion oldRota;
            public Vector3    oldScale;
        }

        // List which contains required details to track and keep objects in sync.
        private Dictionary<int, RemoteObjectInfoStruct> remoteObjectDict = new Dictionary<int, RemoteObjectInfoStruct>();
        
        /// <summary>
        /// Keep a list of the remote heads, indexed by XTools userID
        /// </summary>
        private Dictionary<long, RemoteHeadInfo> remoteHeads = new Dictionary<long, RemoteHeadInfo>();

        private void Start()
        {
            SyncMessaging.Instance.MessageHandlers[SyncMessaging.TestMessageID.HeadTransform] = UpdateHeadTransform;
            SyncMessaging.Instance.MessageHandlers[SyncMessaging.TestMessageID.ObjectTransform] = UpdateObjectTransform;

            // SharingStage should be valid at this point.
            SharingStage.Instance.SharingManagerConnected += Connected;

            // Loop through all gameObjects that gotta stay synched, create structs for them and save their transform.
            for (int i = 0; i < listOfGameObjToKeepSynced.Count; i++)
            {
                RemoteObjectInfoStruct objInfoStruct;
                objInfoStruct = new RemoteObjectInfoStruct();
                objInfoStruct.objNumber = i;

                // Now add this to the Dict.
                remoteObjectDict.Add(i, objInfoStruct);
                // After struct creation, init values.
                AssignOldTransform(i); 
            }
        }

        private void Connected(object sender, EventArgs e)
        {
            SharingStage.Instance.SharingManagerConnected -= Connected;

            SharingStage.Instance.SessionUsersTracker.UserJoined += UserJoinedSession;
            SharingStage.Instance.SessionUsersTracker.UserLeft += UserLeftSession;
        }
        
        private void Update()
        {
            // Have not tested performance, doing only every 2nd frame for now.
            if (Time.frameCount % 2 == 0)
            {
                // Grab the current head transform and broadcast it to all the other users in the session
                Transform headTransform = Camera.main.transform;

                // Transform the head position and rotation from world space into local space
                Vector3 headPosition = transform.InverseTransformPoint(headTransform.position);
                Quaternion headRotation = Quaternion.Inverse(transform.rotation) * headTransform.rotation;

                SyncMessaging.Instance.SendHeadTransform(headPosition, headRotation);

                // Check whether any transforms have changed.
                // Basically read every single transform in the List and compare it to what we've saved in our Struct.
                for (int i = 0; i < listOfGameObjToKeepSynced.Count; i++)
                {
                    if (HasTransformChanged(i) == true)
                    {
                        // Collect transform data and send out Transform to others.
                        Vector3 objectPosition = listOfGameObjToKeepSynced[i].transform.localPosition;
                        Quaternion objectRotation = listOfGameObjToKeepSynced[i].transform.localRotation;
                        Vector3 objectScale = listOfGameObjToKeepSynced[i].transform.localScale;

                        SyncMessaging.Instance.SendObjectTransform(objectPosition, objectRotation, objectScale, i);

                        // Finally, update "old" transform so it can be compared against new transform changes.
                        AssignOldTransform(i);
                    }
                }
            }
            
        } //Update end.

        /// <summary>
        /// Compares an objects current transform values to the saved ones in order to detect transform/position changes.
        /// </summary>
        /// <param name="objNum">Array position of the object we seek.</param>
        private bool HasTransformChanged(int objNum)
        {
            // Pull out the struct we compare against.
            RemoteObjectInfoStruct structToCompareAgainst = GetRemoteObjectInfo(objNum);

            if (structToCompareAgainst.oldPos != listOfGameObjToKeepSynced[objNum].transform.localPosition)
            {
                Debug.Log("Position change detected @ listOfGameObjToKeepSynced[" + objNum + "]");
                return true;
            }

            // Rotation is double checked due to problems at small angles.
            // aaRotation.Equals(bbRotation) == false && (aaRotation != bbRotation)
            if (structToCompareAgainst.oldRota != listOfGameObjToKeepSynced[objNum].transform.localRotation
                && structToCompareAgainst.oldRota.Equals(listOfGameObjToKeepSynced[objNum].transform.localRotation) == false)
            {
                Debug.Log("Rotation change detected @ listOfGameObjToKeepSynced[" + objNum + "]");
                return true;
            }

            if (structToCompareAgainst.oldScale != listOfGameObjToKeepSynced[objNum].transform.localScale)
            {
                Debug.Log("Scale change detected @ listOfGameObjToKeepSynced[" + objNum + "]");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Saves a copy of current object positions, which will then be compared against to detect position changes of said objects.
        /// </summary>
        /// <param name="objNumber">Array position of the object we seek.</param>
        private void AssignOldTransform(int objNum)
        {
            // Pull out the struct which we want to update.
            RemoteObjectInfoStruct structToUpdate = GetRemoteObjectInfo(objNum);
            structToUpdate.oldPos   = listOfGameObjToKeepSynced[objNum].transform.localPosition;
            structToUpdate.oldRota  = listOfGameObjToKeepSynced[objNum].transform.localRotation;
            structToUpdate.oldScale = listOfGameObjToKeepSynced[objNum].transform.localScale;
        }

        /// <summary>
        /// Gets the data structure of a particular object.
        /// </summary>
        /// <param name="objNumber">Array position of the object we seek.</param>
        public RemoteObjectInfoStruct GetRemoteObjectInfo(int objNumber)
        {
            RemoteObjectInfoStruct objectInfo;
            // Get the one we seek.
            remoteObjectDict.TryGetValue(objNumber, out objectInfo);

            return objectInfo;
        }

        protected override void OnDestroy()
        {
            if (SharingStage.Instance != null)
            {
                if (SharingStage.Instance.SessionUsersTracker != null)
                {
                    SharingStage.Instance.SessionUsersTracker.UserJoined -= UserJoinedSession;
                    SharingStage.Instance.SessionUsersTracker.UserLeft   -= UserLeftSession;
                }
            }

            base.OnDestroy();
        }

        /// <summary>
        /// Called when a new user is leaving the current session.
        /// </summary>
        /// <param name="user">User that left the current session.</param>
        private void UserLeftSession(User user)
        {
            int userId = user.GetID();
            if (userId != SharingStage.Instance.Manager.GetLocalUser().GetID())
            {
                RemoveRemoteHead(remoteHeads[userId].HeadObject);
                remoteHeads.Remove(userId);
            }
        }

        /// <summary>
        /// Called when a user is joining the current session.
        /// </summary>
        /// <param name="user">User that joined the current session.</param>
        private void UserJoinedSession(User user)
        {
            if (user.GetID() != SharingStage.Instance.Manager.GetLocalUser().GetID())
            {
                GetRemoteHeadInfo(user.GetID());
            }
        }

        /// <summary>
        /// Gets the data structure for the remote users' head position.
        /// </summary>
        /// <param name="userId">User ID for which the remote head info should be obtained.</param>
        /// <returns>RemoteHeadInfo for the specified user.</returns>
        public RemoteHeadInfo GetRemoteHeadInfo(long userId)
        {
            RemoteHeadInfo headInfo;

            // Get the head info if its already in the list, otherwise add it
            if (!remoteHeads.TryGetValue(userId, out headInfo))
            {
                headInfo = new RemoteHeadInfo();
                headInfo.UserID = userId;
                headInfo.HeadObject = CreateRemoteHead();

                remoteHeads.Add(userId, headInfo);
            }

            return headInfo;
        }

        /// <summary>
        /// Called when a remote user sends a head transform.
        /// </summary>
        /// <param name="msg"></param>
        private void UpdateHeadTransform(NetworkInMessage msg)
        {
            // Parse the message
            long userID = msg.ReadInt64();

            Vector3 headPos = SyncMessaging.Instance.ReadVector3(msg);

            Quaternion headRot = SyncMessaging.Instance.ReadQuaternion(msg);

            RemoteHeadInfo headInfo = GetRemoteHeadInfo(userID);
            headInfo.HeadObject.transform.localPosition = headPos;
            headInfo.HeadObject.transform.localRotation = headRot;
        }

        /// <summary>
        /// Called when a remote user sends an object transform.
        /// </summary>
        /// <param name="msg"></param>
        private void UpdateObjectTransform(NetworkInMessage msg)
        { 
            // Parse the message

            // get userID, but we're not using it
            long userID = msg.ReadInt64();

            // get Position 
            Vector3 objectPos = SyncMessaging.Instance.ReadVector3(msg);
            // get Rotation
            Quaternion objectRota = SyncMessaging.Instance.ReadQuaternion(msg);
            // get Scale
            Vector3 objectScale = SyncMessaging.Instance.ReadVector3(msg);
            // Read out object number.
            var objNumber = msg.ReadInt16();

            // Now we know which objNumber and the new transform, so we update ours.
            listOfGameObjToKeepSynced[objNumber].transform.localPosition = objectPos;
            listOfGameObjToKeepSynced[objNumber].transform.localRotation = objectRota;
            listOfGameObjToKeepSynced[objNumber].transform.localScale    = objectScale;
            // Finally, set "old" = "new" so that it can detect new changes in position.
            AssignOldTransform(objNumber);
        }

        /// <summary>
        /// Creates a new game object to represent the user's head.
        /// </summary>
        /// <returns></returns>
        private GameObject CreateRemoteHead()
        {
            GameObject newHeadObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            newHeadObj.transform.parent = gameObject.transform;
            newHeadObj.transform.localScale = Vector3.one * 0.2f;
            newHeadObj.GetComponent<Renderer>().material.color = new Color(0, 255, 0); //RGB
            return newHeadObj;
        }

        /// <summary>
        /// When a user has left the session this will cleanup their
        /// head data.
        /// </summary>
        /// <param name="remoteHeadObject"></param>
        private void RemoveRemoteHead(GameObject remoteHeadObject)
        {
            DestroyImmediate(remoteHeadObject);
        }
    }
}