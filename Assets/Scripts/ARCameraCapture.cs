using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using DG.Tweening;
using Immersal.XR;

public class ARPhotoCapture : MonoBehaviour
{
    public ARCameraManager cameraManager;      // AR camera manager from ARFoundation
    public GameObject photoFramePrefab;        // Prefab for photo frame
    public ARRaycastManager raycastManager;    // For plane detection
    private float initFrameDistance = 0.1f;
    public float frameDistance = 0.5f;         // Default distance when photoframe is created

    private GameObject currentPhotoFrame;      // Current photo frame object
    private Texture2D capturedTexture;         // Image captured from the camera
    private Camera arCamera;
    private bool isDragging = false;           
    
    private Immersal.XR.XRSpace xrSpace;
    
    private List<GameObject> photoFrames = new List<GameObject>();
    private int currentMapId;
    private GameObject currentMapObj;
    
    private HashSet<int> loadedMaps = new HashSet<int>();

    private void Awake()
    {
        if (xrSpace == null)
            xrSpace = GameObject.FindObjectOfType<Immersal.XR.XRSpace>();
    }

    void Start()
    {
        arCamera = Camera.main;
        
        if (GetComponent<ARRaycastManager>() == null)
            Debug.LogError("ARRaycastManager not found!");
        if (GetComponent<ARPlaneManager>() == null)
            Debug.LogError("ARPlaneManager not found!");
    }

    /**
     * When 'capture' button is pressed
     */
    public void TakePhoto()
    {
        StartCoroutine(CapturePhoto());
    }

    IEnumerator CapturePhoto()
    {
        if (cameraManager.TryAcquireLatestCpuImage(out XRCpuImage image))
        {
            XRCpuImage.ConversionParams conversionParams = new XRCpuImage.ConversionParams
            {
                inputRect = new RectInt(0, 0, image.width, image.height),
                outputDimensions = new Vector2Int(image.width, image.height),
                outputFormat = TextureFormat.RGBA32,
                transformation = XRCpuImage.Transformation.MirrorX
            };

            // Creating a Texture2D for saving image from the camera
            capturedTexture = new Texture2D(image.width, image.height, TextureFormat.RGBA32, false);
            image.Convert(conversionParams, capturedTexture.GetRawTextureData<byte>());
            capturedTexture.Apply();
            
            string frameId = System.Guid.NewGuid().ToString();

            // Initialize a photoframe object
            currentPhotoFrame = Instantiate(photoFramePrefab);
            currentPhotoFrame.name = frameId;
            currentPhotoFrame.transform.SetParent(currentMapObj.transform);

            // set the initial pose of the photoframe
            Vector3 startPosition = arCamera.transform.position + arCamera.transform.forward * initFrameDistance;
            currentPhotoFrame.transform.position = startPosition;
            currentPhotoFrame.transform.LookAt(arCamera.transform);
            currentPhotoFrame.transform.Rotate(0, 180, 0);
            photoFrames.Add(currentPhotoFrame);

            // aspect ratio for adjusting the photoframe according to the image
            float aspectRatio = (float)capturedTexture.height / capturedTexture.width;
            Transform photoFrame = FindChildRecursive(currentPhotoFrame.transform, "Frame");
            Transform photoPlane = FindChildRecursive(currentPhotoFrame.transform, "Plane");
            if (photoFrame != null && photoPlane != null)
            {
                Renderer renderer = photoPlane.GetComponent<Renderer>();
                if (renderer != null)
                    renderer.material.mainTexture = capturedTexture; //applying camera image
                
                photoFrame.localScale = new Vector3(photoFrame.localScale.y * aspectRatio, photoFrame.localScale.y, photoFrame.localScale.z);
                photoPlane.localScale = new Vector3(photoPlane.localScale.x, photoPlane.localScale.y, photoPlane.localScale.x * aspectRatio);
            }

            // Saving image to PNG file, for being able to restore later.
            string imagePath = SaveImageAsPNG(capturedTexture, frameId + ".png", currentMapId.ToString());

            // Saving data of the photoframe
            SaveNewPhotoFrame(imagePath, currentPhotoFrame);

            // Move photoframe
            Vector3 targetPosition = arCamera.transform.position + arCamera.transform.forward * frameDistance;
            currentPhotoFrame.transform.DOMove(targetPosition, 1f)
                .SetEase(Ease.OutBack)
                .OnComplete(() => 
                {
                    // TODO: enable interaction button on the photoframe?
                });

            // Releasing XRCpuImage
            image.Dispose();
        }

        yield return null;
    }
    
    void Update()
    {
        if (photoFrames.Count == 0)
            return;

        // Detecting touch events
        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);

            if (touch.phase == TouchPhase.Began)
            {
                // Detecting raycast on the photoframe object
                Ray ray = arCamera.ScreenPointToRay(touch.position);
                RaycastHit hit;
                if (Physics.Raycast(ray, out hit))
                {
                    foreach (var photoFrame in photoFrames)
                    {
                        if (hit.transform == photoFrame.transform)
                        {
                            currentPhotoFrame = photoFrame;
                            isDragging = true;
                            break;
                        }
                    }
                }
            }
            else if (touch.phase == TouchPhase.Moved && isDragging)
            {
                // Plane detection for placing photoframe when user dragging
                List<ARRaycastHit> hits = new List<ARRaycastHit>();
                if (raycastManager.Raycast(touch.position, hits, TrackableType.PlaneWithinPolygon))
                {
                    Pose hitPose = hits[0].pose;
                    currentPhotoFrame.transform.position = hitPose.position;
                    currentPhotoFrame.transform.rotation = hitPose.rotation;
                    currentPhotoFrame.transform.Rotate(90, 0, 0);
                }
            }
            else if (touch.phase == TouchPhase.Ended)
            {
                isDragging = false;
                UpdatePhotoFramePosition(currentPhotoFrame);
            }
        }
    }
    
    Transform FindChildRecursive(Transform parent, string childName)
    {
        if (parent.name == childName)
            return parent;
        
        foreach (Transform child in parent)
        {
            Transform result = FindChildRecursive(child, childName);
            if (result != null)
                return result;
        }
        return null;
    }
    
    /**
     * Saving image as PNG file
     */
    private string SaveImageAsPNG(Texture2D texture, string fileName, string mapID)
    {
        string folderPath = Path.Combine(Application.persistentDataPath, "images_" + mapID);
        
        if (!Directory.Exists(folderPath))
            Directory.CreateDirectory(folderPath);
        
        string filePath = Path.Combine(folderPath, fileName);
        
        byte[] bytes = texture.EncodeToPNG();
        File.WriteAllBytes(filePath, bytes);

        Debug.Log("Saved image to: " + filePath);
        return filePath;
    }

    /**
     * Saving data for newly created photoframe
     */
    public void SaveNewPhotoFrame(string imagePath, GameObject photoFrame)
    {
        // Try reading existing config file
        string path = Application.persistentDataPath + $"/contents_{currentMapId}.json";
        PhotoFrameCollection photoFrameCollection;

        // Load config if existing
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            photoFrameCollection = JsonUtility.FromJson<PhotoFrameCollection>(json);
        }
        else
        {
            photoFrameCollection = new PhotoFrameCollection();
        }

        // Creating data for the photoframe
        Transform photoPlane = FindChildRecursive(photoFrame.transform, "Plane");
        PhotoFrameData newData = new PhotoFrameData
        {
            frameId = photoFrame.name,  // 使用相框名称作为 frameId
            localPosition = photoFrame.transform.localPosition,
            localRotation = photoFrame.transform.localRotation,
            aspectRatio = photoPlane.localScale.z / photoPlane.localScale.x,
            imagePath = imagePath
        };
        
        photoFrameCollection.photoFrames.Add(newData);
        string newJson = JsonUtility.ToJson(photoFrameCollection, true);
        File.WriteAllText(path, newJson);

        Debug.Log("New photo frame saved to: " + path);
    }
    
    /**
     * Updating data for photoframe
     */
    public void UpdatePhotoFramePosition(GameObject photoFrame)
    {
        // Reading existing config file
        string path = Application.persistentDataPath + $"/contents_{currentMapId}.json";
        PhotoFrameCollection photoFrameCollection;

        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            photoFrameCollection = JsonUtility.FromJson<PhotoFrameCollection>(json);

            // Finding the photoframe that user is dragging
            PhotoFrameData existingData = photoFrameCollection.photoFrames.Find(data => data.frameId == photoFrame.name);

            if (existingData != null)
            {
                existingData.localPosition = photoFrame.transform.localPosition;
                existingData.localRotation = photoFrame.transform.localRotation;
            }

            // Update data back to config file
            string updatedJson = JsonUtility.ToJson(photoFrameCollection, true);
            File.WriteAllText(path, updatedJson);

            Debug.Log("Updated photo frame position in JSON for frameId: " + photoFrame.name);
        }
        else
        {
            Debug.LogError("No JSON file found to update.");
        }
    }
    
    public void LoadPhotoFrames()
    {
        // Config file path
        string path = Application.persistentDataPath + $"/contents_{currentMapId}.json";

        // Reading config if existing
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);

            PhotoFrameCollection photoFrameCollection = JsonUtility.FromJson<PhotoFrameCollection>(json);

            // Iterate and initialize every photoframe object
            foreach (var frameData in photoFrameCollection.photoFrames)
            {
                GameObject photoFrame = Instantiate(photoFramePrefab);
                photoFrame.name = frameData.frameId;

                photoFrame.transform.SetParent(currentMapObj.transform);

                // Restoring the pose for each photoframe
                photoFrame.transform.localPosition = frameData.localPosition;
                photoFrame.transform.localRotation = frameData.localRotation;

                // Restoring the aspect ratio
                Transform frame = FindChildRecursive(photoFrame.transform, "Frame");
                Transform plane = FindChildRecursive(photoFrame.transform, "Plane");
                
                if (frame != null && plane != null)
                {
                    frame.localScale = new Vector3(frame.localScale.y * frameData.aspectRatio, frame.localScale.y, frame.localScale.z);
                    plane.localScale = new Vector3(plane.localScale.x, plane.localScale.y, plane.localScale.x * frameData.aspectRatio);

                    // Applying image to the photoframe
                    if (!string.IsNullOrEmpty(frameData.imagePath) && File.Exists(frameData.imagePath))
                    {
                        byte[] imageBytes = File.ReadAllBytes(frameData.imagePath);
                        Texture2D loadedTexture = new Texture2D(2, 2);
                        loadedTexture.LoadImage(imageBytes);
                        
                        ApplyTextureToPlane(photoFrame, loadedTexture);
                    }
                }

                photoFrames.Add(photoFrame);
            }

            Debug.Log("Photo frames loaded from: " + path);
        }
        else
        {
            Debug.Log($"No saved photo frames found for MapID: {currentMapId}");
        }
    }

    /**
     * Applying image to photoframe object
     */
    private void ApplyTextureToPlane(GameObject photoFrame, Texture2D texture)
    {
        Transform photoPlane = FindChildRecursive(photoFrame.transform, "Plane");
        if (photoPlane != null)
        {
            Renderer renderer = photoPlane.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.mainTexture = texture;
            }
        }
    }

    /**
     * Deleting data
     */
    public void Clear()
    {
        foreach (var photoFrame in photoFrames)
        {
            Destroy(photoFrame);
        }

        photoFrames.Clear();
        
        string imagesDirectory = Path.Combine(Application.persistentDataPath, "images_" + currentMapId);
        if (Directory.Exists(imagesDirectory))
        {
            Directory.Delete(imagesDirectory, true);  // true = recursively deleting
            Debug.Log($"Deleted image directory: {imagesDirectory}");
        }
        else
        {
            Debug.LogWarning($"No image directory found for MapID: {currentMapId}");
        }

        // Deleting config file
        string jsonPath = Path.Combine(Application.persistentDataPath, $"contents_{currentMapId}.json");

        if (File.Exists(jsonPath))
        {
            File.Delete(jsonPath);
            Debug.Log($"Deleted JSON file: {jsonPath}");
        }
        else
        {
            Debug.LogWarning($"No JSON file found to delete for MapID: {currentMapId}");
        }
    }

    public void OnLocalized(int[] mapId)
    {
        if (mapId.Length <= 0)
            return;
        
        //the latest localized map
        int newMapId = mapId[mapId.Length - 1];
        Debug.Log($">>> Current localized mapId = {newMapId}");

        GameObject newMapObj = null; 
        XRMap[] xrmaps = xrSpace.GetComponentsInChildren<XRMap>();
        for (int i = 0; i < xrmaps.Length; i++)
        {
            XRMap map = xrmaps[i];
            if (map.mapId == newMapId)
            {
                newMapObj = map.gameObject;
                Debug.Log($">>> Found map object for {map.mapId}");
                break;
            }
        }

        if (newMapObj != null)
        {
            currentMapObj = newMapObj;
            currentMapId = newMapId;
        }
        else
        {
            Debug.LogWarning("No map object found for the localized map.");
            return;
        }
    
        Debug.Log($"result: current mapId = {currentMapId}, current map object = {currentMapObj.GetInstanceID()}");
    
        // Load only one time for each map
        if (!loadedMaps.Contains(currentMapId))
        {
            LoadPhotoFrames();
            loadedMaps.Add(currentMapId);
        }
        else
        {
            Debug.Log($"Map {currentMapId} has already been loaded.");
        }
    }
}

[System.Serializable]
public class PhotoFrameData
{
    public Vector3 localPosition;
    public Quaternion localRotation;
    public float aspectRatio;
    public string imagePath;
    public string frameId;
}

[System.Serializable]
public class PhotoFrameCollection
{
    public List<PhotoFrameData> photoFrames = new List<PhotoFrameData>();
}