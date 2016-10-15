using ColossalFramework;
using ColossalFramework.Math;
using ICities;
using UnityEngine;
using UnityStandardAssets.ImageEffects;

namespace FPSCamera
{
    public class FPSCamera : MonoBehaviour
    {

        public delegate void OnCameraModeChanged(bool state);

        public static OnCameraModeChanged onCameraModeChanged;

        public delegate void OnUpdate();

        public static OnUpdate onUpdate;

        public static bool editorMode = false;

        public static void Initialize(LoadMode mode)
        {
            var controller = GameObject.FindObjectOfType<CameraController>();
            instance = controller.gameObject.AddComponent<FPSCamera>();

            if (mode == LoadMode.LoadGame || mode == LoadMode.NewGame)
            {
                instance.gameObject.AddComponent<GamePanelExtender>();
                instance.vehicleCamera = instance.gameObject.AddComponent<VehicleCamera>();
                instance.citizenCamera = instance.gameObject.AddComponent<CitizenCamera>();
                instance.citizenCamera.vehicleCamera = instance.vehicleCamera;
                editorMode = false;
            }
            else
            {
                editorMode = true;
            }
        }

        public static void Deinitialize()
        {
            Destroy(instance);
        }

        public static FPSCamera instance;

        public static readonly string configPath = "FPSCameraConfig.xml";
        public Configuration config;

        public bool fpsModeEnabled = false;
        private CameraController controller;
        public Camera camera;
        float rotationY = 0f;
        public DepthOfField effect;
        public TiltShiftEffect legacyEffect;

        private Vector3 mainCameraPosition;
        private Quaternion mainCameraOrientation;

        private SavedInputKey cameraMoveLeft;
        private SavedInputKey cameraMoveRight;
        private SavedInputKey cameraMoveForward;
        private SavedInputKey cameraMoveBackward;
        private SavedInputKey cameraZoomCloser;
        private SavedInputKey cameraZoomAway;

        public bool checkedForHideUI = false;

        public VehicleCamera vehicleCamera;
        public CitizenCamera citizenCamera;
        
        public bool cityWalkthroughMode = false;
        private float cityWalkthroughNextChangeTimer = 0.0f;

        public float originalFieldOfView = 0.0f;

        public bool isIsometric;

        public FPSCameraUI ui;

        void Start()
        {
            controller = FindObjectOfType<CameraController>();
            camera = controller.GetComponent<Camera>();
            originalFieldOfView = camera.fieldOfView;
            effect = controller.GetComponent<DepthOfField>();
            legacyEffect = controller.GetComponent<TiltShiftEffect>();

            config = Configuration.Deserialize(configPath);
            if (config == null)
            {
                config = new Configuration();
            }

            SaveConfig();

            mainCameraPosition = gameObject.transform.position;
            mainCameraOrientation = gameObject.transform.rotation;

            cameraMoveLeft = new SavedInputKey(Settings.cameraMoveLeft, Settings.gameSettingsFile, DefaultSettings.cameraMoveLeft, true);
            cameraMoveRight = new SavedInputKey(Settings.cameraMoveRight, Settings.gameSettingsFile, DefaultSettings.cameraMoveRight, true);
            cameraMoveForward = new SavedInputKey(Settings.cameraMoveForward, Settings.gameSettingsFile, DefaultSettings.cameraMoveForward, true);
            cameraMoveBackward = new SavedInputKey(Settings.cameraMoveBackward, Settings.gameSettingsFile, DefaultSettings.cameraMoveBackward, true);
            cameraZoomCloser = new SavedInputKey(Settings.cameraZoomCloser, Settings.gameSettingsFile, DefaultSettings.cameraZoomCloser, true);
            cameraZoomAway = new SavedInputKey(Settings.cameraZoomAway, Settings.gameSettingsFile, DefaultSettings.cameraZoomAway, true);

            mainCameraPosition = gameObject.transform.position;
            mainCameraOrientation = gameObject.transform.rotation;
            rotationY = -instance.transform.localEulerAngles.x;

            ui = FPSCameraUI.Instance;
        }

        public void SaveConfig()
        {
            Configuration.Serialize(configPath, config);
        }

        void GUICheckbox(string label, ref bool state)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.ExpandWidth(false));
            state = GUILayout.Toggle(state, "");
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private Matrix4x4 sliderOffsetMatrix = Matrix4x4.TRS(new Vector3(0.0f, 6.0f, 0.0f), Quaternion.identity, Vector3.one);

        void GUISlider(string label, ref float state, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.ExpandWidth(false));

            var oldMatrix = GUI.matrix;
            GUI.matrix *= sliderOffsetMatrix;

            state = GUILayout.HorizontalSlider(state, min, max);

            GUI.matrix = oldMatrix;

            GUILayout.Label(state.ToString("0.00"), GUILayout.ExpandWidth(false));
            GUILayout.EndHorizontal();
        }

        public void SetFieldOfView(float fov)
        {
            config.fieldOfView = fov;
            SaveConfig();
            if (fpsModeEnabled)
            {
                camera.fieldOfView = fov;
            }
        }

        private bool inModeTransition = false;
        private Vector3 transitionTargetPosition = Vector3.zero;
        private Quaternion transitionTargetOrientation = Quaternion.identity;
        private Vector3 transitionStartPosition = Vector3.zero;
        private Quaternion transitionStartOrientation = Quaternion.identity;
        private float transitionT = 0.0f;

        public void EnterWalkthroughMode()
        {
            cityWalkthroughMode = true;
            cityWalkthroughNextChangeTimer = config.walkthroughModeTimer;

            if ( config.integrateHideUI)
            {
                UIHider.Hide();
            }

            WalkthroughModeSwitchTarget();
            FPSCameraUI.Instance.Hide();
        }

        public void ResetConfig()
        {
            config = new Configuration();
            SaveConfig();

            Destroy(FPSCameraUI.instance);
            FPSCameraUI.instance = null;
            ui = FPSCameraUI.Instance;
            ui.Show();
        }

        public void SetMode(bool fpsMode)
        {
            instance.fpsModeEnabled = fpsMode;
            FPSCameraUI.instance.Hide();

            if (config.integrateHideUI)
            {
                if (instance.fpsModeEnabled)
                {
                    UIHider.Hide();
                }
                else
                {
                    UIHider.Show();
                }
            }

            if (instance.fpsModeEnabled)
            {
                camera.fieldOfView = config.fieldOfView;
                instance.controller.m_maxDistance = 50f;
                instance.controller.m_currentSize = 5;
                instance.controller.m_currentHeight =2f;
                instance.controller.enabled = false;

                if( effect)
                {
                    effect.enabled = config.enableDOF;
                }
                if( legacyEffect)
                {
                    legacyEffect.enabled = false;
                }

                Cursor.visible = false;
                instance.rotationY = -instance.transform.localEulerAngles.x;
            }
            else
            {
                instance.controller.m_maxDistance = 10000f;

                if (!config.animateTransitions)
                {
                    instance.controller.enabled = true;
                }

                camera.fieldOfView = originalFieldOfView;
                Cursor.visible = true;
                effect.nearBlur = true;
            }
            
            onCameraModeChanged?.Invoke(fpsMode);
        }

        public static KeyCode GetToggleUIKey()
        {
            return instance.config.toggleFPSCameraHotkey;
        }

        public static bool IsEnabled()
        {
            return instance.fpsModeEnabled;
        }

        public ushort GetRandomVehicle()
        {
            var vmanager = VehicleManager.instance;
            int skip = Random.Range(0, vmanager.m_vehicleCount - 1);

            for (ushort i = 0; i < vmanager.m_vehicles.m_buffer.Length; i++)
            {
                if ((vmanager.m_vehicles.m_buffer[i].m_flags & (Vehicle.Flags.Created | Vehicle.Flags.Deleted)) != Vehicle.Flags.Created)
                {
                    continue;
                }

                if (vmanager.m_vehicles.m_buffer[i].Info.m_vehicleAI is CarTrailerAI)
                {
                    continue;
                }

                if(skip > 0)
                {
                    skip--;
                    continue;
                }

                return i;
            }

            for (ushort i = 0; i < vmanager.m_vehicles.m_buffer.Length; i++)
            {
                if ((vmanager.m_vehicles.m_buffer[i].m_flags & (Vehicle.Flags.Created | Vehicle.Flags.Deleted)) !=
                    Vehicle.Flags.Created)
                {
                    continue;
                }

                if (vmanager.m_vehicles.m_buffer[i].Info.m_vehicleAI is CarTrailerAI)
                {
                    continue;
                }

                return i;
            }

            return 0;
        }
        
        public uint GetRandomCitizenInstance()
        {
            var cmanager = CitizenManager.instance;
            int skip = Random.Range(0, cmanager.m_instanceCount - 1);

            for (uint i = 0; i < cmanager.m_instances.m_buffer.Length; i++)
            {
                if ((cmanager.m_instances.m_buffer[i].m_flags & (CitizenInstance.Flags.Created | CitizenInstance.Flags.Deleted)) != CitizenInstance.Flags.Created)
                {
                    continue;
                }

                if (skip > 0)
                {
                    skip--;
                    continue;
                }

                return cmanager.m_instances.m_buffer[i].m_citizen;
            }

            for (uint i = 0; i < cmanager.m_instances.m_buffer.Length; i++)
            {
                if ((cmanager.m_instances.m_buffer[i].m_flags & (CitizenInstance.Flags.Created | CitizenInstance.Flags.Deleted)) != CitizenInstance.Flags.Created)
                {
                    continue;
                }

                return cmanager.m_instances.m_buffer[i].m_citizen;
            }

            return 0;
        }

        void WalkthroughModeSwitchTarget()
        {
            bool vehicleOrCitizen = Random.Range(0, 3) == 0;
            if (!vehicleOrCitizen)
            {
                if (citizenCamera.following)
                {
                    citizenCamera.StopFollowing();
                }

                var vehicle = GetRandomVehicle();
                if (vehicle != 0)
                {
                    vehicleCamera.SetFollowInstance(vehicle);
                }
            }
            else
            {
                if (vehicleCamera.following)
                {
                    vehicleCamera.StopFollowing();
                }

                var citizen = GetRandomCitizenInstance();
                if (citizen != 0)
                {
                    citizenCamera.SetFollowInstance(citizen);
                }
            }
        }

        void UpdateCityWalkthrough()
        {
            if (cityWalkthroughMode && !config.walkthroughModeManual)
            {
                cityWalkthroughNextChangeTimer -= Time.deltaTime;
                if (cityWalkthroughNextChangeTimer <= 0.0f || !(citizenCamera.following || vehicleCamera.following))
                {
                    cityWalkthroughNextChangeTimer = config.walkthroughModeTimer;
                    WalkthroughModeSwitchTarget();
                }
            }
            else if (cityWalkthroughMode)
            {
                if (Input.GetMouseButtonDown(0))
                {
                    WalkthroughModeSwitchTarget();
                }
            }
        }

        void UpdateCameras()
        {
            if (vehicleCamera != null && vehicleCamera.following && config.allowUserOffsetInVehicleCitizenMode)
            {
                if (cameraMoveForward.IsPressed())
                {
                    vehicleCamera.userOffset += gameObject.transform.forward * config.cameraMoveSpeed * 0.25f * Time.deltaTime;
                }
                else if (cameraMoveBackward.IsPressed())
                {
                    vehicleCamera.userOffset -= gameObject.transform.forward * config.cameraMoveSpeed * 0.25f * Time.deltaTime;
                }

                if (cameraMoveLeft.IsPressed())
                {
                    vehicleCamera.userOffset -= gameObject.transform.right * config.cameraMoveSpeed * 0.25f * Time.deltaTime;
                }
                else if (cameraMoveRight.IsPressed())
                {
                    vehicleCamera.userOffset += gameObject.transform.right * config.cameraMoveSpeed * 0.25f * Time.deltaTime;
                }

                if (cameraZoomAway.IsPressed())
                {
                    vehicleCamera.userOffset -= gameObject.transform.up * config.cameraMoveSpeed * 0.25f * Time.deltaTime;
                }
                else if (cameraZoomCloser.IsPressed())
                {
                    vehicleCamera.userOffset += gameObject.transform.up * config.cameraMoveSpeed * 0.25f * Time.deltaTime;
                }
            }

            if (citizenCamera != null && citizenCamera.following && config.allowUserOffsetInVehicleCitizenMode)
            {
                if (cameraMoveForward.IsPressed())
                {
                    citizenCamera.userOffset += gameObject.transform.forward * config.cameraMoveSpeed * 0.25f * Time.deltaTime;
                }
                else if (cameraMoveBackward.IsPressed())
                {
                    citizenCamera.userOffset -= gameObject.transform.forward * config.cameraMoveSpeed * 0.25f * Time.deltaTime;
                }

                if (cameraMoveLeft.IsPressed())
                {
                    citizenCamera.userOffset -= gameObject.transform.right * config.cameraMoveSpeed * 0.25f * Time.deltaTime;
                }
                else if (cameraMoveRight.IsPressed())
                {
                    citizenCamera.userOffset += gameObject.transform.right * config.cameraMoveSpeed * 0.25f * Time.deltaTime;
                }

                if (cameraZoomAway.IsPressed())
                {
                    citizenCamera.userOffset -= gameObject.transform.up * config.cameraMoveSpeed * 0.25f * Time.deltaTime;
                }
                else if (cameraZoomCloser.IsPressed())
                {
                    citizenCamera.userOffset += gameObject.transform.up * config.cameraMoveSpeed * 0.25f * Time.deltaTime;
                }
            }
        }

        void OnEscapePressed()
        {
            if (cityWalkthroughMode)
            {

                cityWalkthroughMode = false;
                if( config.integrateHideUI)
                {
                    UIHider.Hide();
                }

                if (vehicleCamera != null && vehicleCamera.following)
                {
                    vehicleCamera.StopFollowing();
                }

                if (citizenCamera != null && citizenCamera.following)
                {
                    citizenCamera.StopFollowing();
                }
                
            }
            else
            {
                if (vehicleCamera != null && vehicleCamera.following)
                {
                    if (config.integrateHideUI)
                    {
                        UIHider.Hide();
                    }
                    vehicleCamera.StopFollowing();
                }
                if (citizenCamera != null && citizenCamera.following)
                {
                    if (config.integrateHideUI)
                    {
                        UIHider.Hide();
                    }
                    citizenCamera.StopFollowing();
                }
                if (fpsModeEnabled)
                {
                    if (config.animateTransitions && fpsModeEnabled)
                    {
                        inModeTransition = true;
                        transitionT = 0.0f;

                        if ((gameObject.transform.position - mainCameraPosition).magnitude <= 1.0f)
                        {
                            transitionT = 1.0f;
                            mainCameraOrientation = gameObject.transform.rotation;
                        }

                        transitionStartPosition = gameObject.transform.position;
                        transitionStartOrientation = gameObject.transform.rotation;

                        transitionTargetPosition = mainCameraPosition;
                        transitionTargetOrientation = mainCameraOrientation;
                    }

                    SetMode(!fpsModeEnabled);
                }
            }
            
            ui.Hide();
        }

        void OnToggleCameraHotkeyPressed()
        {
            if (cityWalkthroughMode)
            {
                if (config.integrateHideUI)
                {
                    UIHider.Show();
                }

                cityWalkthroughMode = false;
                if (vehicleCamera.following)
                {
                    vehicleCamera.StopFollowing();
                }
                if (citizenCamera.following)
                {
                    citizenCamera.StopFollowing();
                }


            }
            else if (vehicleCamera != null && vehicleCamera.following)
            {
                vehicleCamera.StopFollowing();
            }
            else if (citizenCamera != null && citizenCamera.following)
            {
                citizenCamera.StopFollowing();
            }
            else
            {
                if (config.animateTransitions && fpsModeEnabled)
                {
                    inModeTransition = true;
                    transitionT = 0.0f;

                    if ((gameObject.transform.position - mainCameraPosition).magnitude <= 1.0f)
                    {
                        transitionT = 1.0f;
                        mainCameraOrientation = gameObject.transform.rotation;
                    }

                    transitionStartPosition = gameObject.transform.position;
                    transitionStartOrientation = gameObject.transform.rotation;

                    transitionTargetPosition = mainCameraPosition;
                    transitionTargetOrientation = mainCameraOrientation;
                }

                SetMode(!fpsModeEnabled);
            }
            ui.Hide();
        }

        void LateUpdate()
        {
            if (!fpsModeEnabled && !cityWalkthroughMode && !vehicleCamera.following && !citizenCamera.following)
            {
                if (isIsometric)
                {
                    effect.enabled = false;
                    legacyEffect.enabled = false;
                }

            }
        }

        void Update()
        {
            onUpdate?.Invoke();

            if (!fpsModeEnabled && !cityWalkthroughMode && !vehicleCamera.following && !citizenCamera.following)
            {
                if (Input.GetKeyDown(KeyCode.F9))
                {
                    isIsometric = !isIsometric;

                }
                camera.fieldOfView = isIsometric ? 8 : 45;
                controller.m_maxDistance = isIsometric ? 1500 : 4000;
                if (isIsometric)
                {
                    effect.enabled = false;
                    legacyEffect.enabled = false;

                }

            }

            UpdateCityWalkthrough();

            UpdateCameras();
            
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                OnEscapePressed();
            }

            if (Input.GetKeyDown(config.toggleFPSCameraHotkey))
            {
                OnToggleCameraHotkeyPressed();
            }

            var pos = gameObject.transform.position;

            float terrainY = TerrainManager.instance.SampleDetailHeight(gameObject.transform.position);
            float waterY = TerrainManager.instance.WaterLevel(new Vector2(gameObject.transform.position.x, gameObject.transform.position.z));
            terrainY = Mathf.Max(terrainY, waterY);

            if (config.animateTransitions && inModeTransition)
            {
                transitionT += Time.deltaTime * config.animationSpeed;

                gameObject.transform.position = Vector3.Slerp(transitionStartPosition, transitionTargetPosition, transitionT);
                gameObject.transform.rotation = Quaternion.Slerp(transitionStartOrientation, transitionTargetOrientation, transitionT);

                if (transitionT >= 1.0f)
                {
                    inModeTransition = false;

                    if (!fpsModeEnabled)
                    {
                        instance.controller.enabled = true;
                    }
                }
            }
            else if (fpsModeEnabled)
            {
                if (config.snapToGround)
                {
                    Segment3 ray = new Segment3(gameObject.transform.position + new Vector3(0f, 1.5f, 0f), gameObject.transform.position + new Vector3(0f, -1000f, 0f));

                    Vector3 hitPos;
                    ushort nodeIndex;
                    ushort segmentIndex;
                    Vector3 hitPos2;

                    if (NetManager.instance.RayCast(null, ray, 0f, ItemClass.Service.Road, ItemClass.Service.PublicTransport, ItemClass.SubService.None, ItemClass.SubService.None, ItemClass.Layer.Default, ItemClass.Layer.None, NetNode.Flags.None, NetSegment.Flags.None, out hitPos, out nodeIndex, out segmentIndex)
                        | NetManager.instance.RayCast(null, ray, 0f, ItemClass.Service.Beautification, ItemClass.Service.Water, ItemClass.SubService.None, ItemClass.SubService.None, ItemClass.Layer.Default, ItemClass.Layer.None, NetNode.Flags.None, NetSegment.Flags.None, out hitPos2, out nodeIndex, out segmentIndex))
                    {
                        terrainY = Mathf.Max(terrainY, Mathf.Max(hitPos.y, hitPos2.y));
                    }

                    gameObject.transform.position = Vector3.Lerp(gameObject.transform.position, new Vector3(pos.x, terrainY + config.groundOffset, pos.z), 0.9f);
                }

                float speedFactor = 1.0f;
                if (config.limitSpeedGround)
                {
                    speedFactor *= Mathf.Sqrt(terrainY);
                    speedFactor = Mathf.Clamp(speedFactor, 1.0f, 256.0f);
                }

                if (Input.GetKey(config.goFasterHotKey))
                {
                    speedFactor *= config.goFasterSpeedMultiplier;
                }

                if (cameraMoveForward.IsPressed())
                {
                    gameObject.transform.position += gameObject.transform.forward * config.cameraMoveSpeed * speedFactor * Time.deltaTime;
                }
                else if (cameraMoveBackward.IsPressed())
                {
                    gameObject.transform.position -= gameObject.transform.forward * config.cameraMoveSpeed * speedFactor * Time.deltaTime;
                }

                if (cameraMoveLeft.IsPressed())
                {
                    gameObject.transform.position -= gameObject.transform.right * config.cameraMoveSpeed * speedFactor * Time.deltaTime;
                }
                else if (cameraMoveRight.IsPressed())
                {
                    gameObject.transform.position += gameObject.transform.right * config.cameraMoveSpeed * speedFactor * Time.deltaTime;
                }

                if (cameraZoomAway.IsPressed())
                {
                    gameObject.transform.position -= gameObject.transform.up * config.cameraMoveSpeed * speedFactor * Time.deltaTime;
                }
                else if (cameraZoomCloser.IsPressed())
                {
                    gameObject.transform.position += gameObject.transform.up * config.cameraMoveSpeed * speedFactor * Time.deltaTime;
                }

                if (Input.GetKey(config.showMouseHotkey))
                {
                    Cursor.visible = true;
                }
                else
                {
                    float rotationX = transform.localEulerAngles.y + Input.GetAxis("Mouse X") * config.cameraRotationSensitivity;
                    rotationY += Input.GetAxis("Mouse Y") * config.cameraRotationSensitivity * (config.invertYAxis ? -1.0f : 1.0f);
                    transform.localEulerAngles = new Vector3(-rotationY, rotationX, 0);
                    Cursor.visible = false;
                }

                camera.fieldOfView = config.fieldOfView;
                camera.nearClipPlane = 1.0f;

                if( effect )
                {
                    effect.enabled = config.enableDOF;
                    if (config.enableDOF)
                    {
                        ushort buildingIndex;
                        ushort vehicleIndex;
                        ushort parkedVehicleIndex;

                        Vector3 hitPos;
                        Segment3 scanSegment = new Segment3(camera.transform.position, camera.transform.position + camera.transform.forward * 1000);
                        if (BuildingManager.instance.RayCast(scanSegment, ItemClass.Service.None, ItemClass.SubService.None, ItemClass.Layer.Default, Building.Flags.None, out hitPos, out buildingIndex))
                        {
                            effect.focalLength = Mathf.Abs(Vector3.Magnitude(hitPos - camera.transform.position));
                        }
                        if (VehicleManager.instance.RayCast(scanSegment, Vehicle.Flags.Deleted, VehicleParked.Flags.None, out hitPos, out vehicleIndex, out parkedVehicleIndex))
                        {
                            effect.focalLength = Mathf.Abs(Vector3.Magnitude(hitPos - camera.transform.position));
                        }
                    }
                }
                float height = FPSCamera.instance.camera.transform.position.y - TerrainManager.instance.SampleDetailHeight(FPSCamera.instance.camera.transform.position);
                float otherHeight = TerrainManager.instance.SampleRawHeightSmoothWithWater(FPSCamera.instance.camera.transform.position, true, 2f);
                RenderManager.instance.CameraHeight = height;

            }
            else
            {
                mainCameraPosition = camera.transform.position;
                mainCameraOrientation = camera.transform.rotation;
            }

            if (config.preventClipGround)
            {
                Segment3 ray = new Segment3(gameObject.transform.position + new Vector3(0f, 1.5f, 0f), gameObject.transform.position + new Vector3(0f, -1000f, 0f));

                Vector3 hitPos;
                ushort nodeIndex;
                ushort segmentIndex;
                Vector3 hitPos2;

                if (NetManager.instance.RayCast(null, ray, 0f, ItemClass.Service.Road, ItemClass.Service.PublicTransport, ItemClass.SubService.None, ItemClass.SubService.None, ItemClass.Layer.Default, ItemClass.Layer.None, NetNode.Flags.None, NetSegment.Flags.None, out hitPos, out nodeIndex, out segmentIndex)
                    | NetManager.instance.RayCast(null, ray, 0f, ItemClass.Service.Beautification, ItemClass.Service.Water, ItemClass.SubService.None, ItemClass.SubService.None, ItemClass.Layer.Default, ItemClass.Layer.None, NetNode.Flags.None, NetSegment.Flags.None, out hitPos2, out nodeIndex, out segmentIndex))
                {
                    terrainY = Mathf.Max(terrainY, Mathf.Max(hitPos.y, hitPos2.y));
                }

                if (transform.position.y < terrainY + config.groundOffset)
                {
                    transform.position = new Vector3(transform.position.x, terrainY + config.groundOffset, transform.position.z);
                }
            }
        }
    }

}
