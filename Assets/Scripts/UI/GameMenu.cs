using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using Unity.Serialization.Json;
using UnityEngine.VFX;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class GameMenu : MonoBehaviour
{
    readonly Dictionary<string, Dictionary<string, string>> localeEntries = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<GameObject, string> languageByObject = new Dictionary<GameObject, string>();
    readonly Dictionary<XRSimpleInteractable, GameObject> objectByInteractable = new Dictionary<XRSimpleInteractable, GameObject>();
    readonly List<GameObject> langObjects = new List<GameObject>();

    MainController mainController;
    bool isActive;
    bool waitingForMenuData;
    string selectedLanguage = "";

    GameObject selectedObject;

    GameObject startBTObject;
    GameObject languagesContainer;
    UnityEngine.XR.Interaction.Toolkit.Interactors.IXRHoverInteractor currentHoverInteractor;
    GameObject hoveredObject;
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
    public float buttonScaling = 1f;
    public float buttonSpacing = 1.2f;
    public float buttonRadius = 2f;

    public float startScaling = 1f;
    public float startYOffset = 2f;
    public float startSmoothing = 5f;
    public float startZRadiusOffset = 1f;
    public Color idleHighlightColor = Color.white;
    public Color selectedHighlightColor = Color.green;
    public bool lookAtCamera = true;
    public bool liveUpdateLayout = true;


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
        if (liveUpdateLayout)
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
            float totalAngle = visibleCount * buttonSpacing;
            float angle = visibleIndex * buttonSpacing - (totalAngle * 0.5f) + (buttonSpacing * 0.5f);

            float radians = angle * Mathf.Deg2Rad;
            float x = Mathf.Sin(radians) * buttonRadius;
            float z = Mathf.Cos(radians) * buttonRadius;
            langObject.transform.localPosition = new Vector3(x, 0f, z);
            langObject.transform.localScale = Vector3.one * buttonScaling;

            if (lookAtCamera && mainCamera != null)
            {
                Vector3 lookAt = new Vector3(transform.position.x, langObject.transform.position.y, transform.position.z);
                langObject.transform.LookAt(lookAt);
            }


            if (selectedObject != null)
            {
                Vector3 targetPosition = selectedObject.transform.position + selectedObject.transform.forward * startZRadiusOffset + Vector3.up * startYOffset;
                Quaternion targetRotation = selectedObject.transform.rotation;
                targetRotation *= Quaternion.Euler(0f, 180f, 0f);
                startBTObject.transform.position = Vector3.Lerp(startBTObject.transform.position, targetPosition, Time.deltaTime * startSmoothing);
                startBTObject.transform.rotation = Quaternion.Slerp(startBTObject.transform.rotation, targetRotation, Time.deltaTime * startSmoothing);
            }

            visibleIndex++;
        }
    }

    void OnDestroy()
    {
        ClearCurrentHover();
        UnregisterStartButton();
        UnregisterLanguageButtons();
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
        LoadLocale();
        Debug.Log("GameMenu refresh - locale keys: " + localeEntries.Count, this);
        startTransitionRunning = false;
        startSelected = false;
        EnsureLanguageButtons();
        selectedLanguage = "";
        UpdateLanguageVisualState();
        UpdateStartButton();
        ResetAllVisuals();
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

    List<string> GetAvailableLanguages()
    {
        HashSet<string> languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (localeEntries.TryGetValue("languages", out Dictionary<string, string> languagesEntry) && languagesEntry != null)
        {
            foreach (string language in languagesEntry.Keys)
            {
                if (!string.IsNullOrWhiteSpace(language))
                {
                    languages.Add(language);
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

        return new List<string>(languages);
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
        TextMeshPro text = langObject.GetComponentInChildren<TextMeshPro>(true);
        if (text != null)
        {
            string label = GetLocalizedText("languages", language);
            text.text = string.Equals(label, "languages", StringComparison.OrdinalIgnoreCase) ? language.ToUpperInvariant() : label;
            text.gameObject.SetActive(true);
        }

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

        bool isStartObject = hoveredObject == startBTObject && !string.IsNullOrWhiteSpace(selectedLanguage);

        if (!isStartObject) hoveredObject.transform.localScale = hoveredOriginalScale;

        VisualEffect vfx = hoveredObject.GetComponentInChildren<VisualEffect>(true);
        if (vfx != null)
        {
            bool isSelectedLanguage = languageByObject.TryGetValue(hoveredObject, out string language) && string.Equals(language, selectedLanguage, StringComparison.OrdinalIgnoreCase);
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
            Debug.Log("ActivateHoveredObject: " + language, this);
            selectedObject = hoveredObject;
        }
    }

    void SetHoveredObject(GameObject targetObject)
    {
        if (hoveredObject == targetObject)
        {
            return;
        }

        ResetObjectAnimation(hoveredObject);
        hoveredObject = targetObject;
        hoveredSince = Time.time;
        hoveredSelected = false;
        hoveredOriginalScale = hoveredObject != null ? hoveredObject.transform.localScale : Vector3.one;
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

        bool isStartObject = targetObject == startBTObject;

        if (!isStartObject) targetObject.transform.localScale = Vector3.one * buttonScaling;

        VisualEffect vfx = targetObject.GetComponentInChildren<VisualEffect>(true);
        if (vfx != null)
        {
            bool isSelectedLanguage = languageByObject.TryGetValue(targetObject, out string language) && string.Equals(language, selectedLanguage, StringComparison.OrdinalIgnoreCase);
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
        }
    }

    void OnLanguageClicked(string language)
    {
        SelectLanguage(language);
        UpdateStartButton();
    }

    void SelectLanguage(string language)
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

        VisualEffect vfx = targetObject.GetComponentInChildren<VisualEffect>(true);
        if (vfx != null)
        {
            vfx.SetFloat("Progression", selected ? 1f : 0f);
            vfx.SetVector4("Highlight Color", selected ? (Vector4)selectedHighlightColor : (Vector4)idleHighlightColor);
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

    string GetLocalizedText(string key, string language)
    {
        if (localeEntries.TryGetValue(key, out Dictionary<string, string> entry) && entry != null)
        {
            if (entry.TryGetValue(language, out string value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            if (entry.TryGetValue("en", out value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return key;
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

        VisualEffect vfx = targetObject.GetComponentInChildren<VisualEffect>(true);
        if (vfx != null)
        {
            vfx.SetFloat("Evaporate", value);
        }

        TextMeshPro text = targetObject.GetComponentInChildren<TextMeshPro>(true);
        if (text != null)
        {
            Color color = text.color;
            color.a = 1f - Mathf.Clamp01(value);
            text.color = color;
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
            Debug.Log("Language selected: " + language, this);
            selectedObject = langObject;
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

    void LoadLocale()
    {
        localeEntries.Clear();

        string localePath = dataManager.GetFilePath(DataManager.DataFolder.Menu, "locale.json");
        if (string.IsNullOrWhiteSpace(localePath) || !File.Exists(localePath))
        {
            return;
        }

        try
        {
            DeserializeLocaleEntries(File.ReadAllText(localePath));
        }
        catch (Exception ex)
        {
            Debug.LogError("Failed to load locale.json: " + ex.Message, this);
        }
    }

    void DeserializeLocaleEntries(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            JsonObject root = JsonSerialization.FromJson<JsonObject>(json);
            if (root == null)
            {
                return;
            }

            Dictionary<string, string> languages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetValue("languages", out object languagesValue) && languagesValue is JsonArray languagesArray)
            {
                for (int i = 0; i < languagesArray.Count; i++)
                {
                    if (!(languagesArray[i] is JsonObject languageObject))
                    {
                        continue;
                    }

                    string code = languageObject.TryGetValue("code", out object codeValue) ? Convert.ToString(codeValue) : string.Empty;
                    string name = languageObject.TryGetValue("name", out object nameValue) ? Convert.ToString(nameValue) : string.Empty;

                    if (!string.IsNullOrWhiteSpace(code))
                    {
                        languages[code] = string.IsNullOrWhiteSpace(name) ? code.ToUpperInvariant() : name;
                    }
                }
            }

            if (languages.Count > 0)
            {
                localeEntries["languages"] = languages;
            }

            if (root.TryGetValue("start", out object startValue) && startValue is JsonObject startObject)
            {
                Dictionary<string, string> startEntries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, object> entry in startObject)
                {
                    string key = entry.Key;
                    string value = Convert.ToString(entry.Value);
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        startEntries[key] = value;
                    }
                }

                if (startEntries.Count > 0)
                {
                    localeEntries["start"] = startEntries;
                }
            }
        }
        catch
        {
        }
    }

    public void GazeHover(HoverEnterEventArgs args)
    {
    }

    public void GazeExit(HoverExitEventArgs args)
    {
    }
}
