﻿using System;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.ARFoundation;

namespace UnityEngine.XR.ARFoundation.Samples
{
    /// <summary>
    /// This component listens for images detected by the <c>XRImageTrackingSubsystem</c>
    /// and overlays some information as well as the source Texture2D on top of the
    /// detected image.
    /// </summary>
    [RequireComponent(typeof(ARTrackedImageManager))]
    public class MultiTrackedImageInfoManager : MonoBehaviour, ISerializationCallbackReceiver
    {
        [Serializable]
        /// <summary>
        /// Used to associate an `XRReferenceImage` with a Prefab by using the `XRReferenceImage`'s guid as a unique identifier for a particular reference image.
        /// </summary>
        struct NamedPrefab
        {
            // System.Guid isn't serializable, so we store the Guid as a string. At runtime, this is converted back to a System.Guid
            [SerializeField]
            string m_ImageGuid;
            public string imageGuid => m_ImageGuid;

            [SerializeField]
            public GameObject m_Prefab;

            public NamedPrefab(XRReferenceImage image, GameObject prefab)
            {
                m_ImageGuid = image.guid.ToString();
                m_Prefab = prefab;
            }

            public NamedPrefab(Guid guid, GameObject prefab)
            {
                m_ImageGuid = guid.ToString();
                m_Prefab = prefab;
            }
        }

        [SerializeField]
        [HideInInspector]
        List<NamedPrefab> m_PrefabsList = new List<NamedPrefab>();

        Dictionary<Guid, GameObject> m_PrefabsDictionary = new Dictionary<Guid, GameObject>();
        Dictionary<Guid, GameObject> m_InstantiatedPrefabsDictionary = new Dictionary<Guid, GameObject>();
        ARTrackedImageManager m_TrackedImageManager;

        [SerializeField]
        [Tooltip("Reference Image Library")]
        XRReferenceImageLibrary m_ImageLibrary;

        /// <summary>
        /// Get the <c>XRReferenceImageLibrary</c>
        /// </summary>
        public XRReferenceImageLibrary imageLibrary
        {
            get => m_ImageLibrary;
            set => m_ImageLibrary = value;
        }

        public void OnBeforeSerialize()
        {
            m_PrefabsList.Clear();
            foreach (var kvp in m_PrefabsDictionary)
            {
                m_PrefabsList.Add(new NamedPrefab(kvp.Key, kvp.Value));
            }
        }

        public void OnAfterDeserialize()
        {
            m_PrefabsDictionary = new Dictionary<Guid, GameObject>();
            foreach (var entry in m_PrefabsList)
            {
                m_PrefabsDictionary.Add(Guid.Parse(entry.imageGuid), entry.m_Prefab);
            }
        }

        void Awake()
        {
            m_TrackedImageManager = GetComponent<ARTrackedImageManager>();
        }

        void OnEnable()
        {
            m_TrackedImageManager.trackedImagesChanged += OnTrackedImagesChanged;
        }

        void OnDisable()
        {
            m_TrackedImageManager.trackedImagesChanged -= OnTrackedImagesChanged;
        }

        void OnTrackedImagesChanged(ARTrackedImagesChangedEventArgs eventArgs)
        {
            foreach (var trackedImage in eventArgs.added)
            {
                // Give the initial image a reasonable default scale
                var minLocalScalar = Mathf.Min(trackedImage.size.x, trackedImage.size.y);
                trackedImage.transform.localScale = new Vector3(minLocalScalar / 2, minLocalScalar / 2, minLocalScalar / 2);
                AssignOrShowPrefab(trackedImage);
            }

            foreach (var trackedImage in eventArgs.updated)
            {
                if (trackedImage.trackingState != TrackingState.Tracking)
                {
                    if (m_InstantiatedPrefabsDictionary.TryGetValue(trackedImage.referenceImage.guid, out GameObject instantiatedPrefab))
                        instantiatedPrefab.SetActive(false);
                }
                else
                    AssignOrShowPrefab(trackedImage);
            }
        }

        void AssignOrShowPrefab(ARTrackedImage trackedImage)
        {
            if (m_PrefabsDictionary.TryGetValue(trackedImage.referenceImage.guid, out GameObject prefab))
            {
                if (!m_InstantiatedPrefabsDictionary.ContainsKey(trackedImage.referenceImage.guid))
                {
                    var instantiatedPrefab = Instantiate(prefab, trackedImage.transform);
                    m_InstantiatedPrefabsDictionary.Add(trackedImage.referenceImage.guid, instantiatedPrefab);
                }
                else
                {
                    var instantiatedPrefab = m_InstantiatedPrefabsDictionary[trackedImage.referenceImage.guid];
                    instantiatedPrefab.SetActive(true);
                }
            }
        }

        public GameObject GetPrefabForReferenceImage(XRReferenceImage referenceImage)
            => m_PrefabsDictionary.TryGetValue(referenceImage.guid, out var prefab) ? prefab : null;

        public void SetPrefabForReferenceImage(XRReferenceImage referenceImage, GameObject alternativePrefab)
        {
            if (m_PrefabsDictionary.TryGetValue(referenceImage.guid, out GameObject targetPrefabInDictionary))
            {
                m_PrefabsDictionary[referenceImage.guid] = alternativePrefab;
                if (m_InstantiatedPrefabsDictionary.TryGetValue(referenceImage.guid, out GameObject instantiatedPrefab))
                {
                    Destroy(instantiatedPrefab);
                    m_InstantiatedPrefabsDictionary.Remove(referenceImage.guid);
                }
            }
        }

#if UNITY_EDITOR

        [CustomEditor(typeof(MultiTrackedImageInfoManager))]
        class MultiTrackedImageInfoManagerInspector : Editor 
        {
            List<XRReferenceImage> m_ReferenceImages = new List<XRReferenceImage>();
            bool m_IsExpanded = true;

            bool HasLibraryChanged(XRReferenceImageLibrary library)
            {
                if (library == null)
                    return m_ReferenceImages.Count == 0;

                if (m_ReferenceImages.Count != library.count)
                    return true;

                for (int i = 0; i < library.count; i++)
                {
                    if (m_ReferenceImages[i] != library[i])
                        return true;
                }

                return false;
            }
            
            public override void OnInspectorGUI () 
            {
                //customized inspector
                var behaviour = serializedObject.targetObject as MultiTrackedImageInfoManager;

                serializedObject.Update();
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
                }
                
                var libraryProperty = serializedObject.FindProperty(nameof(m_ImageLibrary));
                EditorGUILayout.PropertyField(libraryProperty);
                var library = libraryProperty.objectReferenceValue as XRReferenceImageLibrary;

                //check library changes
                if (HasLibraryChanged(library))
                {
                    if (library)
                    {
                        var tempDictionary = new Dictionary<Guid, GameObject>();
                        foreach (var referenceImage in library)
                        {
                            tempDictionary.Add(referenceImage.guid, behaviour.GetPrefabForReferenceImage(referenceImage));
                        }
                        behaviour.m_PrefabsDictionary = tempDictionary;
                    }
                }   

                // update current
                m_ReferenceImages.Clear();
                if (library)
                {
                    foreach (var referenceImage in library)
                    {
                        m_ReferenceImages.Add(referenceImage);
                    }
                }

                //show prefab list
                m_IsExpanded = EditorGUILayout.Foldout(m_IsExpanded, "Prefab List");
                if (m_IsExpanded)
                {
                    using (new EditorGUI.IndentLevelScope())
                    {
                        EditorGUI.BeginChangeCheck();
                    
                        var tempDictionary = new Dictionary<Guid, GameObject>();
                        foreach (var image in library)
                        {
                            var prefab = (GameObject)EditorGUILayout.ObjectField(image.name, behaviour.m_PrefabsDictionary[image.guid], typeof(GameObject), false);
                            tempDictionary.Add(image.guid, prefab);
                        }
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(target, "Update Prefab");
                            behaviour.m_PrefabsDictionary = tempDictionary;
                            EditorUtility.SetDirty(target);
                        }
                    }   
                }

                serializedObject.ApplyModifiedProperties();
	        }
        }
#endif
    }
}
