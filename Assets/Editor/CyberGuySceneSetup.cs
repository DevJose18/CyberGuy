using System.Collections.Generic;
using Autohand;
using Autohand.Demo;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class CyberGuySceneSetup
{
    private const string CyberGuyScenePath = "Assets/Scenes/CyberGuy.unity";
    private const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";
    private const string XRPlayerPrefabPath = "Assets/AutoHand/Examples/Scenes/XR/Prefabs/XRPlayer.prefab";
    private const string SkyboxGuid = "f531e09e2ad2fb440b4eed7605863143";

    public static void SetupScenes()
    {
        SetupCyberGuyScene();
        SetupMainMenuScene();
        UpdateBuildSettings();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[CyberGuySceneSetup] Escenas y Build Settings configurados correctamente.");
    }

    private static void SetupCyberGuyScene()
    {
        Scene scene = EditorSceneManager.OpenScene(CyberGuyScenePath, OpenSceneMode.Single);

        GameManager gameManager = Object.FindFirstObjectByType<GameManager>();
        AutoHandPlayer autoHandPlayer = Object.FindFirstObjectByType<AutoHandPlayer>();
        XRHandPlayerControllerLink controllerLink = Object.FindFirstObjectByType<XRHandPlayerControllerLink>();

        if (gameManager == null)
        {
            Debug.LogError("[CyberGuySceneSetup] No se encontró GameManager en CyberGuy.unity");
            return;
        }

        // Configurar o crear PauseCanvas reutilizando el Canvas vacío si existe
        GameObject pauseCanvasObj = null;
        Canvas existingCanvas = Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
        if (existingCanvas != null && (existingCanvas.gameObject.name == "Canvas" || existingCanvas.gameObject.name == "PauseCanvas"))
        {
            pauseCanvasObj = existingCanvas.gameObject;
            pauseCanvasObj.name = "PauseCanvas";
        }
        else
        {
            pauseCanvasObj = new GameObject("PauseCanvas");
        }

        // Limpiar hijos previos si los hubiera para reconstruir limpiamente
        for (int i = pauseCanvasObj.transform.childCount - 1; i >= 0; i--)
        {
            Object.DestroyImmediate(pauseCanvasObj.transform.GetChild(i).gameObject);
        }

        ConfigureWorldSpaceCanvas(pauseCanvasObj, new Vector2(680f, 560f), 0.002f);

        GameObject panel = CreatePanel(pauseCanvasObj.transform, "Panel", new Color(0.05f, 0.08f, 0.14f, 0.92f));
        CreateAccentBar(panel.transform, new Vector2(0f, 250f), new Vector2(640f, 8f), new Color(0.0f, 0.85f, 1.0f, 1f));
        CreateTMPText(panel.transform, "TitleText", "PAUSA", 56, FontStyles.Bold, new Vector2(0f, 175f), new Vector2(600f, 90f), new Color(0.0f, 0.9f, 1.0f, 1f));

        Button resumeBtn = CreateButton(panel.transform, "ResumeButton", "REANUDAR", new Vector2(0f, 55f), new Vector2(460f, 85f), new Color(0.08f, 0.45f, 0.58f, 1f));
        Button restartBtn = CreateButton(panel.transform, "RestartButton", "REINICIAR", new Vector2(0f, -55f), new Vector2(460f, 85f), new Color(0.14f, 0.22f, 0.34f, 1f));
        Button menuBtn = CreateButton(panel.transform, "MainMenuButton", "MENÚ PRINCIPAL", new Vector2(0f, -165f), new Vector2(460f, 85f), new Color(0.38f, 0.14f, 0.22f, 1f));

        UnityEventTools.AddPersistentListener(resumeBtn.onClick, new UnityAction(gameManager.ResumeGame));
        UnityEventTools.AddPersistentListener(restartBtn.onClick, new UnityAction(gameManager.RestartGame));
        UnityEventTools.AddPersistentListener(menuBtn.onClick, new UnityAction(gameManager.LoadMainMenu));

        pauseCanvasObj.SetActive(false);

        // Asignar referencias en GameManager
        SerializedObject gmSO = new SerializedObject(gameManager);
        gmSO.FindProperty("pauseCanvas").objectReferenceValue = pauseCanvasObj;
        gmSO.FindProperty("autoHandPlayer").objectReferenceValue = autoHandPlayer;
        gmSO.FindProperty("playerControllerLink").objectReferenceValue = controllerLink;
        if (autoHandPlayer != null)
        {
            gmSO.FindProperty("playerTransform").objectReferenceValue = autoHandPlayer.transform;
            if (autoHandPlayer.headCamera != null)
                gmSO.FindProperty("headCameraTransform").objectReferenceValue = autoHandPlayer.headCamera.transform;
        }
        gmSO.ApplyModifiedProperties();
        EditorUtility.SetDirty(gameManager);

        // Agregar XRControllerEvent para el botón de menú / pausa en el control izquierdo
        if (autoHandPlayer != null && controllerLink != null && controllerLink.moveController != null)
        {
            XRControllerEvent[] existingEvents = autoHandPlayer.GetComponents<XRControllerEvent>();
            XRControllerEvent pauseEvent = null;
            foreach (XRControllerEvent evt in existingEvents)
            {
                if (evt.button == CommonButton.menuButton)
                {
                    pauseEvent = evt;
                    break;
                }
            }

            if (pauseEvent == null)
            {
                pauseEvent = autoHandPlayer.gameObject.AddComponent<XRControllerEvent>();
            }

            pauseEvent.link = controllerLink.moveController;
            pauseEvent.button = CommonButton.menuButton;

            // Limpiar listeners previos en Pressed si hubiera y conectar TogglePause
            int listenerCount = pauseEvent.Pressed != null ? pauseEvent.Pressed.GetPersistentEventCount() : 0;
            for (int i = listenerCount - 1; i >= 0; i--)
            {
                UnityEventTools.RemovePersistentListener(pauseEvent.Pressed, i);
            }
            UnityEventTools.AddPersistentListener(pauseEvent.Pressed, new UnityAction(gameManager.TogglePause));
            EditorUtility.SetDirty(pauseEvent);
        }

        EnsureAutoInputModuleEventSystem();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, CyberGuyScenePath);
    }

    private static void SetupMainMenuScene()
    {
        Scene menuScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Configurar Skybox igual que en CyberGuy
        string skyboxPath = AssetDatabase.GUIDToAssetPath(SkyboxGuid);
        if (!string.IsNullOrEmpty(skyboxPath))
        {
            Material skyboxMat = AssetDatabase.LoadAssetAtPath<Material>(skyboxPath);
            if (skyboxMat != null)
            {
                RenderSettings.skybox = skyboxMat;
            }
        }
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;

        // Luz direccional
        GameObject lightObj = new GameObject("Directional Light");
        Light dirLight = lightObj.AddComponent<Light>();
        dirLight.type = LightType.Directional;
        dirLight.intensity = 1.1f;
        lightObj.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        // Piso base en capa Buildings (11) para soporte del XRPlayer
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "MenuFloor";
        floor.layer = LayerMask.NameToLayer("Buildings");
        floor.transform.position = Vector3.zero;
        floor.transform.localScale = new Vector3(4f, 1f, 4f);
        Renderer floorRenderer = floor.GetComponent<Renderer>();
        if (floorRenderer != null)
        {
            Shader urpLit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material floorMat = new Material(urpLit);
            floorMat.color = new Color(0.06f, 0.08f, 0.12f, 1f);
            AssetDatabase.CreateAsset(floorMat, "Assets/Scenes/MainMenuFloor.mat");
            floorRenderer.sharedMaterial = floorMat;
        }

        // Instanciar XRPlayer para interacción VR con UIPointer
        GameObject xrPlayerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(XRPlayerPrefabPath);
        if (xrPlayerPrefab != null)
        {
            GameObject xrPlayerInstance = (GameObject)PrefabUtility.InstantiatePrefab(xrPlayerPrefab);
            xrPlayerInstance.transform.position = Vector3.zero;
            xrPlayerInstance.transform.rotation = Quaternion.identity;

            AutoHandPlayer player = xrPlayerInstance.GetComponentInChildren<AutoHandPlayer>();
            if (player != null)
            {
                player.useMovement = false;
                EditorUtility.SetDirty(player);
            }

            XRHandPlayerControllerLink link = xrPlayerInstance.GetComponentInChildren<XRHandPlayerControllerLink>();
            if (link != null)
            {
                link.enabled = false;
                EditorUtility.SetDirty(link);
            }

            Rigidbody rb = player != null ? player.GetComponent<Rigidbody>() : null;
            if (rb != null)
            {
                rb.constraints = RigidbodyConstraints.FreezeAll;
                EditorUtility.SetDirty(rb);
            }
        }
        else
        {
            GameObject camObj = new GameObject("Main Camera");
            camObj.tag = "MainCamera";
            camObj.AddComponent<Camera>();
            camObj.AddComponent<AudioListener>();
            camObj.transform.position = new Vector3(0f, 1.4f, 0f);
        }

        EnsureAutoInputModuleEventSystem();

        // MainMenuManager
        GameObject managerObj = new GameObject("MainMenuManager");
        MainMenuManager menuManager = managerObj.AddComponent<MainMenuManager>();

        // MainMenuCanvas en World Space frente al jugador
        GameObject canvasObj = new GameObject("MainMenuCanvas");
        ConfigureWorldSpaceCanvas(canvasObj, new Vector2(800f, 620f), 0.002f);
        canvasObj.transform.position = new Vector3(0f, 1.4f, 2.2f);
        canvasObj.transform.rotation = Quaternion.identity;

        GameObject panel = CreatePanel(canvasObj.transform, "Panel", new Color(0.04f, 0.07f, 0.13f, 0.94f));
        CreateAccentBar(panel.transform, new Vector2(0f, 280f), new Vector2(760f, 10f), new Color(0.0f, 0.9f, 1.0f, 1f));
        CreateTMPText(panel.transform, "TitleText", "CYBERGUY", 72, FontStyles.Bold, new Vector2(0f, 185f), new Vector2(700f, 110f), new Color(0.0f, 0.92f, 1.0f, 1f));
        CreateTMPText(panel.transform, "SubtitleText", "MENÚ PRINCIPAL", 32, FontStyles.Normal, new Vector2(0f, 105f), new Vector2(700f, 60f), new Color(0.75f, 0.88f, 0.96f, 1f));

        Button playBtn = CreateButton(panel.transform, "PlayButton", "JUGAR", new Vector2(0f, -20f), new Vector2(480f, 100f), new Color(0.08f, 0.52f, 0.65f, 1f));
        Button quitBtn = CreateButton(panel.transform, "QuitButton", "SALIR", new Vector2(0f, -150f), new Vector2(480f, 95f), new Color(0.42f, 0.14f, 0.22f, 1f));

        UnityEventTools.AddPersistentListener(playBtn.onClick, new UnityAction(menuManager.PlayGame));
        UnityEventTools.AddPersistentListener(quitBtn.onClick, new UnityAction(menuManager.QuitGame));

        EditorSceneManager.MarkSceneDirty(menuScene);
        EditorSceneManager.SaveScene(menuScene, MainMenuScenePath);
    }

    private static void UpdateBuildSettings()
    {
        List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>
        {
            new EditorBuildSettingsScene(MainMenuScenePath, true),
            new EditorBuildSettingsScene(CyberGuyScenePath, true)
        };
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    private static void EnsureAutoInputModuleEventSystem()
    {
        EventSystem eventSystem = Object.FindFirstObjectByType<EventSystem>();
        if (eventSystem == null)
        {
            GameObject esObj = new GameObject("EventSystem");
            eventSystem = esObj.AddComponent<EventSystem>();
        }

        if (eventSystem.GetComponent<AutoInputModule>() == null)
        {
            eventSystem.gameObject.AddComponent<AutoInputModule>();
        }

        BaseInputModule[] modules = eventSystem.GetComponents<BaseInputModule>();
        foreach (BaseInputModule mod in modules)
        {
            if (!(mod is AutoInputModule))
            {
                Object.DestroyImmediate(mod);
            }
        }
    }

    private static void ConfigureWorldSpaceCanvas(GameObject canvasObj, Vector2 sizeDelta, float scale)
    {
        int uiLayer = LayerMask.NameToLayer("UI");
        canvasObj.layer = uiLayer;

        Canvas canvas = canvasObj.GetComponent<Canvas>() ?? canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        CanvasScaler scaler = canvasObj.GetComponent<CanvasScaler>() ?? canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.dynamicPixelsPerUnit = 10f;

        if (canvasObj.GetComponent<GraphicRaycaster>() == null)
            canvasObj.AddComponent<GraphicRaycaster>();

        RectTransform rect = canvasObj.GetComponent<RectTransform>();
        rect.sizeDelta = sizeDelta;
        rect.localScale = Vector3.one * scale;
        rect.anchoredPosition3D = Vector3.zero;
    }

    private static GameObject CreatePanel(Transform parent, string name, Color color)
    {
        GameObject panel = new GameObject(name);
        panel.layer = parent.gameObject.layer;
        panel.transform.SetParent(parent, false);

        RectTransform rt = panel.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        Image img = panel.AddComponent<Image>();
        img.color = color;
        return panel;
    }

    private static void CreateAccentBar(Transform parent, Vector2 anchoredPos, Vector2 size, Color color)
    {
        GameObject bar = new GameObject("AccentBar");
        bar.layer = parent.gameObject.layer;
        bar.transform.SetParent(parent, false);

        RectTransform rt = bar.AddComponent<RectTransform>();
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;

        Image img = bar.AddComponent<Image>();
        img.color = color;
    }

    private static TextMeshProUGUI CreateTMPText(Transform parent, string name, string text, float fontSize, FontStyles style, Vector2 anchoredPos, Vector2 size, Color color)
    {
        GameObject obj = new GameObject(name);
        obj.layer = parent.gameObject.layer;
        obj.transform.SetParent(parent, false);

        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;

        TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = color;
        return tmp;
    }

    private static Button CreateButton(Transform parent, string name, string label, Vector2 anchoredPos, Vector2 size, Color normalColor)
    {
        GameObject btnObj = new GameObject(name);
        btnObj.layer = parent.gameObject.layer;
        btnObj.transform.SetParent(parent, false);

        RectTransform rt = btnObj.AddComponent<RectTransform>();
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;

        Image img = btnObj.AddComponent<Image>();
        img.color = Color.white;

        Button btn = btnObj.AddComponent<Button>();
        btn.targetGraphic = img;

        ColorBlock colors = btn.colors;
        colors.normalColor = normalColor;
        colors.highlightedColor = Color.Lerp(normalColor, Color.white, 0.25f);
        colors.pressedColor = Color.Lerp(normalColor, Color.black, 0.25f);
        colors.selectedColor = colors.highlightedColor;
        colors.colorMultiplier = 1f;
        btn.colors = colors;

        TextMeshProUGUI tmp = CreateTMPText(btnObj.transform, "Text (TMP)", label, 36, FontStyles.Bold, Vector2.zero, size, Color.white);
        RectTransform tmpRT = tmp.GetComponent<RectTransform>();
        tmpRT.anchorMin = Vector2.zero;
        tmpRT.anchorMax = Vector2.one;
        tmpRT.offsetMin = Vector2.zero;
        tmpRT.offsetMax = Vector2.zero;

        return btn;
    }
}
