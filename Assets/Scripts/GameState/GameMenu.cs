using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.VFX;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class GameMenu : MonoBehaviour
{
    readonly Dictionary<GameObject, string> languageByObject = new Dictionary<GameObject, string>();
    readonly Dictionary<XRSimpleInteractable, GameObject> objectByInteractable = new Dictionary<XRSimpleInteractable, GameObject>();
    readonly Dictionary<GameObject, Transform> titleByObject = new Dictionary<GameObject, Transform>();
    readonly Dictionary<GameObject, Renderer> titleRendererByObject = new Dictionary<GameObject, Renderer>();
    readonly Dictionary<GameObject, Vector3> titleStartPositionByObject = new Dictionary<GameObject, Vector3>();
    readonly Dictionary<GameObject, Vector3> titleTargetPositionByObject = new Dictionary<GameObject, Vector3>();
    readonly Dictionary<GameObject, float> titleAnimStartedAtByObject = new Dictionary<GameObject, float>();
    readonly Dictionary<string, Texture2D> languageTextures = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
    readonly List<GameObject> langObjects = new List<GameObject>();

    const int MenuTextureAnisoLevel = 8;
    MainController mainController;
    bool isActive;
    bool waitingForMenuData;
    string selectedLanguage = "";

    GameObject startBTObject;
    GameObject languagesContainer;
    UnityEngine.XR.Interaction.Toolkit.Interactors.IXRHoverInteractor currentHoverInteractor;
    GameObject hoveredObject;
    GameObject selectedObject;
    float hoveredSince;
    bool hoveredSelected;
    Vector3 hoveredOriginalScale = Vector3.one;
    bool startTransitionRunning;
    bool startSelected;
    float startTransitionStartedAt;

    public GameObject langBTPrefab;
    public float hoverSelectTime = 1f;
    public float startHoverTime = 1f;
    public float evaporateTime = 0.75f;
    public float nextMenuDelay = 2f;
    public float buttonScaling = 0.2f;
    public float buttonSpacing = 1.2f;
    public bool autoButtonSpacing = true;
    public float buttonRadius = 2f;
    public Vector3 startBTOffset = new Vector3(0f, 0f, 2.5f);
    public Vector3 startBTRotOffset = new Vector3(0f, 180f, 0f);
    public Color idleHighlightColor = Color.white;
    public Color selectedHighlightColor = Color.green;
    public bool lookAtCamera = true;
    public bool liveUpdateLayout = true;

    public float selectAnimTime = 1f;
    float timeAtSelection = 0;
    public AnimationCurve titleAnimCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [FormerlySerializedAs("titleSelectedPosition")]
    public Vector3 titleSelectionPosition = new Vector3(0f, 0.25f, 0f);


    [Header("Audio")]
    public AudioEventRefSO langHoverSO;
    public AudioRTPCRefSO langProgressionSO;
    public AudioEventRefSO langSelectSO;
    public AudioEventRefSO startHoverSO;
    public AudioRTPCRefSO startProgressionSO;
    public AudioEventRefSO startSelectSO;

    DataManager dataManager;
    void OnEnable()
    {
        mainController = GameObject.FindAnyObjectByType<MainController>();
        dataManager = GameObject.FindAnyObjectByType<DataManager>();
        CacheReferences();
    }

    void OnValidate()
    {
        CacheReferences();
        ApplyLayout();
    }

    void Update()
    {
        if (liveUpdateLayout || (Application.isPlaying && isActive))
        {
            ApplyLayout();
        }

        if (!isActive || !Application.isPlaying)
        {
            return;
        }

        UpdateStartTransition();

        UpdateHoveredObjectAnimation();
    }

    void ApplyLayout()
    {
        Camera mainCamera = Camera.main;

        int visibleCount = 0;
        for (int i = 0; i < langObjects.Count; i++)
        {
            if (langObjects[i] != null && langObjects[i].activeSelf)
            {
                visibleCount++;
            }
        }

        int visibleIndex = 0;
        for (int i = 0; i < langObjects.Count; i++)
        {
            GameObject langObject = langObjects[i];
            if (langObject == null || !langObject.activeSelf)
            {
                continue;
            }

            //float centeredX = (visibleIndex - ((visibleCount - 1) * 0.5f)) * buttonSpacing;
            //langObject.transform.localPosition = new Vector3(centeredX, 0f, 0f);
            //langObject.transform.localScale = Vector3.one * buttonScaling;


            //rotate around this object
            float bSpacing = autoButtonSpacing ? 360f / visibleCount : buttonSpacing;
            float angle = visibleIndex * bSpacing;

            float radians = angle * Mathf.Deg2Rad;
            Vector3 worldOffset = new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians)) * buttonRadius;
            Transform parentTransform = langObject.transform.parent;
            Vector3 localOffset = parentTransform != null ? parentTransform.InverseTransformDirection(worldOffset) : worldOffset;
            langObject.transform.localPosition = localOffset;
            langObject.transform.localScale = Vector3.one * buttonScaling;

            if (lookAtCamera && mainCamera != null)
            {
                Vector3 lookAt = new Vector3(transform.position.x, langObject.transform.position.y, transform.position.z);
                langObject.transform.LookAt(lookAt);
            }


            visibleIndex++;
        }

        for (int i = 0; i < langObjects.Count; i++)
        {
            UpdateTitleAnimation(langObjects[i]);
        }

        if (selectedObject != null)
        {
            float selectionProgress = selectAnimTime > 0f ? Mathf.Clamp01((Time.time - timeAtSelection) / selectAnimTime) : 1f;
            float halfProgress = Mathf.Clamp01(selectionProgress * 5f);
            float secondHalfProgress = Mathf.Clamp01((selectionProgress - 0.5f) * 2f);

            VisualEffect selectedVfx = selectedObject.GetComponentInChildren<VisualEffect>(true);
            if (selectedVfx != null)
            {
                selectedVfx.SetFloat("Evaporate", halfProgress);
            }

            VisualEffect startVfx = startBTObject != null ? startBTObject.GetComponent<VisualEffect>() : null;
            if (startVfx != null)
            {
                startVfx.SetFloat("SpawnRate", secondHalfProgress);
                startVfx.SetFloat("ParticleSize", secondHalfProgress);
            }

            if (secondHalfProgress > 0)
            {
                Vector3 targetPos = selectedObject.transform.TransformPoint(startBTOffset);
                Quaternion targetRot = selectedObject.transform.rotation * Quaternion.Euler(startBTRotOffset);
                if (startBTObject != null)
                {
                    startBTObject.transform.position = targetPos;
                    startBTObject.transform.rotation = targetRot;
                }
            }
        }
    }

    void OnDestroy()
    {
        ClearCurrentHover();
        UnregisterStartButton();
        UnregisterLanguageButtons();
        ReleaseLanguageTextures();
        ResetAudioProgression();
    }

    public void setActive(bool active)
    {
        if (!Application.isPlaying)
        {
            return;
        }

        isActive = active;
        CacheReferences();

        Collider collider = GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = active;
        }

        SetMenuObjectsActive(active);

        if (!isActive)
        {
            ClearCurrentHover();
            ResetAudioProgression();
            return;
        }

        EnsureInitialized();
        PositionInFrontOfCamera();

        if (!dataManager.IsFolderReady(DataManager.DataFolder.Menu))
        {
            if (!waitingForMenuData)
            {
                waitingForMenuData = true;
                dataManager.PreloadFolder(DataManager.DataFolder.Menu, (success, path) =>
                {
                    waitingForMenuData = false;
                    if (success && isActive)
                    {
                        RefreshMenuData();
                    }
                });
            }

            return;
        }

        RefreshMenuData();
    }

    void CacheReferences()
    {
        if (startBTObject == null)
        {
            Transform startTransform = transform.Find("StartBT");
            if (startTransform != null)
            {
                startBTObject = startTransform.gameObject;
            }
        }

        if (languagesContainer == null)
        {
            Transform languagesTransform = transform.Find("Languages");
            if (languagesTransform != null)
            {
                languagesContainer = languagesTransform.gameObject;
            }
        }
    }

    void EnsureInitialized()
    {
        RegisterStartButton();
        EnsureExistingLanguageObjectsCached();
    }

    void RefreshMenuData()
    {
        selectedLanguage = mainController != null ? mainController.language : "";
        selectedObject = null;
        ReleaseLanguageTextures();
        startTransitionRunning = false;
        startSelected = false;
        EnsureLanguageButtons();
        UpdateLanguageVisualState();
        UpdateStartButton();
        ResetAllVisuals();
        ResetAudioProgression();
        ApplyLayout();
    }

    void SetMenuObjectsActive(bool active)
    {
        if (languagesContainer != null)
        {
            languagesContainer.SetActive(active);
        }

        for (int i = 0; i < langObjects.Count; i++)
        {
            if (langObjects[i] != null)
            {
                langObjects[i].SetActive(active);
            }
        }

        if (startBTObject != null)
        {
            startBTObject.SetActive(active && !string.IsNullOrWhiteSpace(selectedLanguage));
        }
    }

    void RegisterStartButton()
    {
        if (startBTObject == null)
        {
            return;
        }

        XRSimpleInteractable interactable = startBTObject.GetComponent<XRSimpleInteractable>();
        if (interactable == null)
        {
            return;
        }

        interactable.selectEntered.RemoveListener(OnStartSelected);
        interactable.selectEntered.AddListener(OnStartSelected);

        interactable.hoverEntered.RemoveListener(OnStartHoverEntered);
        interactable.hoverEntered.AddListener(OnStartHoverEntered);

        interactable.hoverExited.RemoveListener(OnStartHoverExited);
        interactable.hoverExited.AddListener(OnStartHoverExited);
    }

    void UnregisterStartButton()
    {
        if (startBTObject == null)
        {
            return;
        }

        XRSimpleInteractable interactable = startBTObject.GetComponent<XRSimpleInteractable>();
        if (interactable == null)
        {
            return;
        }

        interactable.selectEntered.RemoveListener(OnStartSelected);
        interactable.hoverEntered.RemoveListener(OnStartHoverEntered);
        interactable.hoverExited.RemoveListener(OnStartHoverExited);
    }

    void EnsureExistingLanguageObjectsCached()
    {
        if (languagesContainer == null || langObjects.Count > 0)
        {
            return;
        }

        for (int i = 0; i < languagesContainer.transform.childCount; i++)
        {
            GameObject child = languagesContainer.transform.GetChild(i).gameObject;
            if (child != null)
            {
                langObjects.Add(child);
            }
        }
    }

    void EnsureLanguageButtons()
    {
        if (languagesContainer == null || langBTPrefab == null)
        {
            if (languagesContainer == null)
            {
                Debug.LogWarning("GameMenu could not find child object 'Languages'.", this);
            }

            if (langBTPrefab == null)
            {
                Debug.LogWarning("GameMenu has no 'langBTPrefab' assigned.", this);
            }

            return;
        }

        List<string> languages = GetAvailableLanguages();
        Debug.Log("GameMenu available languages: " + languages.Count + " -> " + string.Join(", ", languages), this);

        while (langObjects.Count < languages.Count)
        {
            GameObject instance = Instantiate(langBTPrefab, languagesContainer.transform);
            instance.name = "LanguageButton_" + langObjects.Count;
            langObjects.Add(instance);
        }

        for (int i = 0; i < langObjects.Count; i++)
        {
            GameObject langObject = langObjects[i];
            if (langObject == null)
            {
                continue;
            }

            bool shouldBeVisible = i < languages.Count;
            langObject.SetActive(isActive && shouldBeVisible);

            if (!shouldBeVisible)
            {
                RemoveLanguageRegistration(langObject);
                continue;
            }

            string language = languages[i];
            langObject.name = "Language_" + language;
            langObject.transform.localRotation = Quaternion.identity;

            RegisterLanguageButton(langObject, language);
            UpdateLanguageButtonContent(langObject, language);
            ResetObjectAnimation(langObject);
        }

        ApplyLayout();
    }

    public List<string> GetAvailableLanguages()
    {
        List<string> languages = new List<string>();

        if (mainController == null)
        {
            mainController = GameObject.FindAnyObjectByType<MainController>();
        }

        if (dataManager == null)
        {
            dataManager = GameObject.FindAnyObjectByType<DataManager>();
        }

        if (dataManager != null)
        {
            string menuFolderPath = dataManager.GetFolderPath(DataManager.DataFolder.Menu);
            if (!string.IsNullOrWhiteSpace(menuFolderPath) && Directory.Exists(menuFolderPath))
            {
                string languagesFilePath = Path.Combine(menuFolderPath, "languages.txt");
                if (File.Exists(languagesFilePath))
                {
                    string[] lines = File.ReadAllLines(languagesFilePath);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string language = lines[i].Trim();
                        if (!string.IsNullOrWhiteSpace(language))
                        {
                            languages.Add(language);
                        }
                    }
                }
                else
                {
                    // Fallback to old method if file doesn't exist
                    HashSet<string> fallbackLanguages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    string[] textureFiles = Directory.GetFiles(menuFolderPath, "*.png", SearchOption.TopDirectoryOnly);
                    for (int i = 0; i < textureFiles.Length; i++)
                    {
                        string fileName = Path.GetFileNameWithoutExtension(textureFiles[i]);
                        if (!string.IsNullOrWhiteSpace(fileName))
                        {
                            fallbackLanguages.Add(fileName);
                        }
                    }
                    languages = new List<string>(fallbackLanguages);
                }
            }
        }

        if (languages.Count == 0 && mainController != null && !string.IsNullOrWhiteSpace(mainController.language))
        {
            languages.Add(mainController.language);
        }

        if (languages.Count == 0)
        {
            languages.Add("en");
        }

        return languages;
    }

    void RegisterLanguageButton(GameObject langObject, string language)
    {
        RemoveLanguageRegistration(langObject);

        XRSimpleInteractable interactable = langObject.GetComponentInChildren<XRSimpleInteractable>();
        if (interactable == null)
        {
            return;
        }

        interactable.selectEntered.AddListener(OnLanguageSelected);
        interactable.hoverEntered.AddListener(OnLanguageHoverEntered);
        interactable.hoverExited.AddListener(OnLanguageHoverExited);

        languageByObject[langObject] = language;
        objectByInteractable[interactable] = langObject;
    }

    void RemoveLanguageRegistration(GameObject langObject)
    {
        if (langObject == null)
        {
            return;
        }

        XRSimpleInteractable interactable = langObject.GetComponentInChildren<XRSimpleInteractable>();
        if (interactable != null)
        {
            interactable.selectEntered.RemoveListener(OnLanguageSelected);
            interactable.hoverEntered.RemoveListener(OnLanguageHoverEntered);
            interactable.hoverExited.RemoveListener(OnLanguageHoverExited);
            objectByInteractable.Remove(interactable);
        }

        languageByObject.Remove(langObject);
    }

    void UnregisterLanguageButtons()
    {
        for (int i = 0; i < langObjects.Count; i++)
        {
            RemoveLanguageRegistration(langObjects[i]);
        }
    }

    void UpdateLanguageButtonContent(GameObject langObject, string language)
    {
        ApplyLanguageTexture(langObject, language);
        SetTitleTargetPosition(langObject, string.Equals(language, selectedLanguage, StringComparison.OrdinalIgnoreCase) ? titleSelectionPosition : Vector3.zero, !Application.isPlaying);

        VisualEffect vfx = langObject.GetComponentInChildren<VisualEffect>(true);
        if (vfx != null)
        {
            vfx.SetFloat("Progression", string.Equals(language, selectedLanguage, StringComparison.OrdinalIgnoreCase) ? 1f : 0f);
            vfx.SetFloat("Evaporate", 0f);
            vfx.SetVector4("Highlight Color", string.Equals(language, selectedLanguage, StringComparison.OrdinalIgnoreCase) ? (Vector4)selectedHighlightColor : (Vector4)idleHighlightColor);
        }
    }

    void UpdateHoveredObjectAnimation()
    {
        if (hoveredObject == null)
        {
            return;
        }

        float hoverDuration = Time.time - hoveredSince;
        float targetHoverTime = hoveredObject == startBTObject ? startHoverTime : hoverSelectTime;
        float normalizedHover = targetHoverTime > 0f ? Mathf.Clamp01(hoverDuration / targetHoverTime) : 1f;
        hoveredObject.transform.localScale = hoveredOriginalScale;

        VisualEffect vfx = hoveredObject.GetComponentInChildren<VisualEffect>(true);
        if (vfx != null)
        {
            bool isSelectedLanguage = languageByObject.TryGetValue(hoveredObject, out string language) && string.Equals(language, selectedLanguage, StringComparison.OrdinalIgnoreCase);
            bool isStartObject = hoveredObject == startBTObject && !string.IsNullOrWhiteSpace(selectedLanguage);
            float baseProgression = isSelectedLanguage ? 1f : 0f;
            vfx.SetFloat("Progression", isStartObject ? normalizedHover : Mathf.Max(baseProgression, normalizedHover));
            if (!isStartObject)
            {
                vfx.SetVector4("Highlight Color", isSelectedLanguage ? (Vector4)selectedHighlightColor : (Vector4)idleHighlightColor);
            }
        }

        if (hoveredObject == startBTObject)
        {
            SetStartButtonProgression(startHoverTime > 0f ? normalizedHover : 0f);
        }

        if (!hoveredSelected && normalizedHover >= 1f)
        {
            hoveredSelected = true;
            ActivateHoveredObject();
        }
    }

void UpdateAudioProgressionState()
{
    if (hoveredObject != null && !hoveredSelected)
    {
        float hoverProgress = GetCurrentHoverProgress();

        if (hoveredObject == startBTObject && !string.IsNullOrWhiteSpace(selectedLanguage))
        {
            SetAudioRtpc(startProgressionSO, hoverProgress);
        }
        else if (languageByObject.TryGetValue(hoveredObject, out string hoveredLanguage))
        {
            SetAudioRtpc(langProgressionSO, hoverProgress);
        }
    }
    else
    {
        SetAudioRtpc(langProgressionSO, 0f);
        SetAudioRtpc(startProgressionSO, 0f);
    }
}

    float GetCurrentHoverProgress()
    {
        if (hoveredObject == null)
        {
            return 0f;
        }

        float hoverDuration = Time.time - hoveredSince;
        float targetHoverTime = hoveredObject == startBTObject ? startHoverTime : hoverSelectTime;
        return targetHoverTime > 0f ? Mathf.Clamp01(hoverDuration / targetHoverTime) : 1f;
    }

    void ActivateHoveredObject()
    {
        if (hoveredObject == null)
        {
            return;
        }

        if (hoveredObject == startBTObject)
        {
            OnStartClicked();
            return;
        }

        if (languageByObject.TryGetValue(hoveredObject, out string language))
        {
            OnLanguageClicked(language);
        }
    }

    void SetHoveredObject(GameObject targetObject)
    {
        if (hoveredObject == targetObject)
        {
            return;
        }

        if (startSelected) return;


        ResetObjectAnimation(hoveredObject);
        hoveredObject = targetObject;
        hoveredSince = Time.time;
        hoveredSelected = false;
        hoveredOriginalScale = hoveredObject != null ? hoveredObject.transform.localScale : Vector3.one;

        if (hoveredObject == startBTObject)
        {
            PostAudioEvent(startHoverSO);
        }
        else if (hoveredObject != null && languageByObject.ContainsKey(hoveredObject))
        {
            PostAudioEvent(langHoverSO);
        }
    }

    void ClearHoveredObject(GameObject targetObject)
    {
        if (hoveredObject == targetObject)
        {
            ClearCurrentHover();
        }
    }

    void ClearCurrentHover()
    {
        currentHoverInteractor = null;
        ResetObjectAnimation(hoveredObject);
        hoveredObject = null;
        hoveredSince = 0f;
        hoveredSelected = false;
        hoveredOriginalScale = Vector3.one;
    }

    void ResetObjectAnimation(GameObject targetObject)
    {
        if (targetObject == null)
        {
            return;
        }

        targetObject.transform.localScale = Vector3.one * buttonScaling;

        VisualEffect vfx = targetObject.GetComponentInChildren<VisualEffect>(true);
        if (vfx != null)
        {
            bool isSelectedLanguage = languageByObject.TryGetValue(targetObject, out string language) && string.Equals(language, selectedLanguage, StringComparison.OrdinalIgnoreCase);
            bool isStartObject = targetObject == startBTObject;
            float progression = isSelectedLanguage ? 1f : 0f;
            vfx.SetFloat("Progression", progression);
            if (!isStartObject)
            {
                vfx.SetFloat("Evaporate", 0f);
                vfx.SetVector4("Highlight Color", progression > 0f ? (Vector4)selectedHighlightColor : (Vector4)idleHighlightColor);
            }
        }

        if (targetObject == startBTObject)
        {
            SetStartButtonProgression(startSelected && !string.IsNullOrWhiteSpace(selectedLanguage) ? 1f : 0f);
            return;
        }

        bool isSelectedLanguageObject = languageByObject.TryGetValue(targetObject, out string selectedLanguageCode) && string.Equals(selectedLanguageCode, selectedLanguage, StringComparison.OrdinalIgnoreCase);
        SetTitleTargetPosition(targetObject, isSelectedLanguageObject ? titleSelectionPosition : Vector3.zero, !Application.isPlaying);
    }

    void OnLanguageClicked(string language)
    {
        SelectLanguage(language);
        UpdateStartButton();
        SetAudioRtpc(langProgressionSO, 0f);
        PostAudioEvent(langSelectSO);
    }

    public void SelectLanguage(string language)
    {
        selectedLanguage = string.IsNullOrWhiteSpace(language) ? "" : language;

        if (mainController != null)
        {
            mainController.language = selectedLanguage;
        }

        UpdateLanguageVisualState();
    }

    void UpdateLanguageVisualState()
    {
        selectedObject = null;

        for (int i = 0; i < langObjects.Count; i++)
        {
            GameObject langObject = langObjects[i];
            if (langObject == null)
            {
                continue;
            }

            bool selected = languageByObject.TryGetValue(langObject, out string language) &&
                string.Equals(language, selectedLanguage, StringComparison.OrdinalIgnoreCase);

            SetObjectSelectedVisual(langObject, selected);
        }
    }

    void SetObjectSelectedVisual(GameObject targetObject, bool selected)
    {
        if (targetObject == null)
        {
            return;
        }

        if (selected)
        {
            selectedObject = targetObject;
            timeAtSelection = Time.time;
        }


        VisualEffect vfx = targetObject.GetComponentInChildren<VisualEffect>(true);

        if (vfx != null)
        {
            vfx.SetFloat("Progression", selected ? 1f : 0f);
            vfx.SetVector4("Highlight Color", selected ? (Vector4)selectedHighlightColor : (Vector4)idleHighlightColor);
            vfx.SetFloat("Evaporate", 0f);
        }

        SetTitleTargetPosition(targetObject, selected ? titleSelectionPosition : Vector3.zero, !Application.isPlaying);

        Collider collider = vfx != null ? vfx.GetComponent<Collider>() : targetObject.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = !selected;
        }

    }

    void UpdateStartButton()
    {
        if (startBTObject == null)
        {
            return;
        }

        bool shouldShow = isActive && !string.IsNullOrWhiteSpace(selectedLanguage);
        startBTObject.SetActive(shouldShow);

        SetStartButtonProgression(startSelected && shouldShow ? 1f : 0f);
    }

    void SetStartButtonProgression(float progression)
    {
        if (startBTObject == null)
        {
            return;
        }

        VisualEffect vfx = startBTObject.GetComponentInChildren<VisualEffect>(true);
        if (vfx != null)
        {
            vfx.SetFloat("Progression", Mathf.Clamp01(progression));
        }
    }

    void OnStartClicked()
    {
        if (startTransitionRunning || string.IsNullOrWhiteSpace(selectedLanguage))
        {
            return;
        }

        if (mainController != null)
        {
            mainController.language = selectedLanguage;
        }

        startTransitionRunning = true;
        startSelected = true;
        startTransitionStartedAt = Time.time;
        hoveredSelected = true;

        SetStartButtonProgression(1f);
        SetAudioRtpc(startProgressionSO, 0f);
        PostAudioEvent(startSelectSO);

        SetAllEvaporate(0f);
    }

    void UpdateStartTransition()
    {
        if (!startTransitionRunning)
        {
            return;
        }

        float progress = evaporateTime > 0f ? Mathf.Clamp01((Time.time - startTransitionStartedAt) / evaporateTime) : 1f;
        SetAllEvaporate(progress);

        if (Time.time - startTransitionStartedAt >= evaporateTime + nextMenuDelay)
        {
            startTransitionRunning = false;

            if (mainController != null)
            {
                mainController.gameState = MainController.GameState.Intro;
            }

            setActive(false);
        }
    }

    void SetAllEvaporate(float value)
    {
        for (int i = 0; i < langObjects.Count; i++)
        {
            SetObjectEvaporate(langObjects[i], value);
        }
    }

    void ResetAllVisuals()
    {
        for (int i = 0; i < langObjects.Count; i++)
        {
            GameObject langObject = langObjects[i];
            if (langObject == null)
            {
                continue;
            }

            SetObjectEvaporate(langObject, 0f);
            ResetObjectAnimation(langObject);
        }

        if (startBTObject != null)
        {
            ResetObjectAnimation(startBTObject);
        }
    }

    void SetObjectEvaporate(GameObject targetObject, float value)
    {
        if (targetObject == null)
        {
            return;
        }

        if (targetObject != selectedObject)
        {
            VisualEffect vfx = targetObject.GetComponentInChildren<VisualEffect>(true);
            if (vfx != null)
            {
                vfx.SetFloat("Evaporate", value);
            }
        }

        SetTitleAlpha(targetObject, 1f - Mathf.Clamp01(value));
    }

    void ApplyLanguageTexture(GameObject langObject, string language)
    {
        Renderer titleRenderer = GetTitleRenderer(langObject);
        if (titleRenderer == null)
        {
            return;
        }

        Texture2D texture = LoadLanguageTexture(language);
        if (texture == null)
        {
            return;
        }

        Material material = titleRenderer.material;
        if (material == null)
        {
            return;
        }

        if (material.HasProperty("_MainTex"))
        {
            material.mainTexture = texture;
        }

        if (material.HasProperty("_BaseMap"))
        {
            material.SetTexture("_BaseMap", texture);
        }
    }

    Texture2D LoadLanguageTexture(string language)
    {
        if (string.IsNullOrWhiteSpace(language) || dataManager == null)
        {
            return null;
        }

        if (languageTextures.TryGetValue(language, out Texture2D cachedTexture) && cachedTexture != null)
        {
            return cachedTexture;
        }

        string texturePath = dataManager.GetFilePath(DataManager.DataFolder.Menu, language + ".png");
        if (string.IsNullOrWhiteSpace(texturePath) || !File.Exists(texturePath))
        {
            Debug.LogWarning("Missing language texture: " + language + ".png", this);
            return null;
        }

        try
        {
            byte[] pngData = File.ReadAllBytes(texturePath);
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            if (!texture.LoadImage(pngData))
            {
                Destroy(texture);
                return null;
            }

            texture.name = language;
            ConfigureLanguageTexture(texture);
            languageTextures[language] = texture;
            return texture;
        }
        catch (Exception ex)
        {
            Debug.LogError("Failed to load language texture '" + language + ".png': " + ex.Message, this);
            return null;
        }
    }

    void ConfigureLanguageTexture(Texture2D texture)
    {
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = texture.mipmapCount > 1 ? FilterMode.Trilinear : FilterMode.Bilinear;
        texture.anisoLevel = MenuTextureAnisoLevel;
    }

    void ReleaseLanguageTextures()
    {
        foreach (KeyValuePair<string, Texture2D> entry in languageTextures)
        {
            if (entry.Value != null)
            {
                Destroy(entry.Value);
            }
        }

        languageTextures.Clear();
    }

    void SetTitleTargetPosition(GameObject langObject, Vector3 targetLocalPosition, bool instant)
    {
        Transform titleTransform = GetTitleTransform(langObject);
        if (titleTransform == null)
        {
            return;
        }

        if (titleTargetPositionByObject.TryGetValue(langObject, out Vector3 currentTarget) && currentTarget == targetLocalPosition && !instant)
        {
            return;
        }

        titleStartPositionByObject[langObject] = titleTransform.localPosition;
        titleTargetPositionByObject[langObject] = targetLocalPosition;
        titleAnimStartedAtByObject[langObject] = Time.time;

        if (instant || !Application.isPlaying || selectAnimTime <= 0f)
        {
            titleTransform.localPosition = targetLocalPosition;
            titleStartPositionByObject[langObject] = targetLocalPosition;
            titleAnimStartedAtByObject[langObject] = 0f;
        }
    }

    void UpdateTitleAnimation(GameObject langObject)
    {
        Transform titleTransform = GetTitleTransform(langObject);
        if (titleTransform == null)
        {
            return;
        }

        if (!titleTargetPositionByObject.TryGetValue(langObject, out Vector3 targetLocalPosition))
        {
            targetLocalPosition = Vector3.zero;
            titleTargetPositionByObject[langObject] = targetLocalPosition;
        }

        if (!Application.isPlaying || selectAnimTime <= 0f)
        {
            titleTransform.localPosition = targetLocalPosition;
            return;
        }

        if (!titleStartPositionByObject.TryGetValue(langObject, out Vector3 startLocalPosition))
        {
            startLocalPosition = titleTransform.localPosition;
        }

        float startedAt = titleAnimStartedAtByObject.TryGetValue(langObject, out float storedStartedAt) ? storedStartedAt : 0f;
        float progress = Mathf.Clamp01((Time.time - startedAt) / selectAnimTime);
        progress = titleAnimCurve.Evaluate(progress);
        titleTransform.localPosition = Vector3.Lerp(startLocalPosition, targetLocalPosition, progress);
    }

    Transform GetTitleTransform(GameObject langObject)
    {
        if (langObject == null)
        {
            return null;
        }

        if (titleByObject.TryGetValue(langObject, out Transform cachedTitle) && cachedTitle != null)
        {
            return cachedTitle;
        }

        Transform container = langObject.transform.Find("Container");
        Transform titleTransform = container != null ? container.Find("Title") : langObject.transform.Find("Title");
        if (titleTransform == null)
        {
            titleTransform = FindChildRecursive(langObject.transform, "Title");
        }

        if (titleTransform != null)
        {
            titleByObject[langObject] = titleTransform;
        }

        return titleTransform;
    }

    Renderer GetTitleRenderer(GameObject langObject)
    {
        if (langObject == null)
        {
            return null;
        }

        if (titleRendererByObject.TryGetValue(langObject, out Renderer cachedRenderer) && cachedRenderer != null)
        {
            return cachedRenderer;
        }

        Transform titleTransform = GetTitleTransform(langObject);
        Renderer titleRenderer = titleTransform != null ? titleTransform.GetComponent<Renderer>() : null;
        if (titleRenderer == null && titleTransform != null)
        {
            titleRenderer = titleTransform.GetComponentInChildren<Renderer>(true);
        }

        if (titleRenderer != null)
        {
            titleRendererByObject[langObject] = titleRenderer;
        }

        return titleRenderer;
    }

    Transform FindChildRecursive(Transform root, string childName)
    {
        if (root == null)
        {
            return null;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (string.Equals(child.name, childName, StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }

            Transform nestedChild = FindChildRecursive(child, childName);
            if (nestedChild != null)
            {
                return nestedChild;
            }
        }

        return null;
    }

    void SetTitleAlpha(GameObject langObject, float alpha)
    {
        Renderer titleRenderer = GetTitleRenderer(langObject);
        if (titleRenderer == null)
        {
            return;
        }

        Material material = titleRenderer.material;
        if (material == null)
        {
            return;
        }

        if (material.HasProperty("_Color"))
        {
            Color color = material.color;
            color.a = alpha;
            material.color = color;
        }

        if (material.HasProperty("_BaseColor"))
        {
            Color color = material.GetColor("_BaseColor");
            color.a = alpha;
            material.SetColor("_BaseColor", color);
        }
    }

    void PositionInFrontOfCamera()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            return;
        }

        transform.rotation = Quaternion.Euler(0f, mainCamera.transform.eulerAngles.y, 0f);
    }

    void OnStartSelected(SelectEnterEventArgs args)
    {
        OnStartClicked();
    }

    void OnStartHoverEntered(HoverEnterEventArgs args)
    {
        currentHoverInteractor = args.interactorObject;
        SetHoveredObject(startBTObject);
    }

    void OnStartHoverExited(HoverExitEventArgs args)
    {
        if (ReferenceEquals(currentHoverInteractor, args.interactorObject))
        {
            ClearHoveredObject(startBTObject);
        }
    }

    void OnLanguageSelected(SelectEnterEventArgs args)
    {
        if (args.interactableObject is XRSimpleInteractable interactable &&
            objectByInteractable.TryGetValue(interactable, out GameObject langObject) &&
            languageByObject.TryGetValue(langObject, out string language))
        {
            OnLanguageClicked(language);
        }
    }

    void OnLanguageHoverEntered(HoverEnterEventArgs args)
    {
        if (args.interactableObject is XRSimpleInteractable interactable &&
            objectByInteractable.TryGetValue(interactable, out GameObject langObject))
        {
            currentHoverInteractor = args.interactorObject;
            SetHoveredObject(langObject);
        }
    }

    void OnLanguageHoverExited(HoverExitEventArgs args)
    {
        if (args.interactableObject is XRSimpleInteractable interactable &&
            objectByInteractable.TryGetValue(interactable, out GameObject langObject) &&
            ReferenceEquals(currentHoverInteractor, args.interactorObject))
        {
            ClearHoveredObject(langObject);
        }
    }

    public void GazeHover(HoverEnterEventArgs args)
    {
    }

    public void GazeExit(HoverExitEventArgs args)
    {
    }

    void LateUpdate()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (!isActive)
        {
            ResetAudioProgression();
            return;
        }

        UpdateAudioProgressionState();
    }

    void ResetAudioProgression()
    {
        SetAudioRtpc(langProgressionSO, 0f);
        SetAudioRtpc(startProgressionSO, 0f);
    }

    void SetAudioRtpc(AudioRTPCRefSO rtpcRef, float value)
    {
        if (rtpcRef == null || rtpcRef.rtpc == null)
        {
            return;
        }

        rtpcRef.rtpc.SetValue(gameObject, Mathf.Clamp01(value));
    }

    void PostAudioEvent(AudioEventRefSO eventRef)
    {
        if (eventRef == null || eventRef.evt == null)
        {
            return;
        }

        eventRef.evt.Post(gameObject);
    }
}
