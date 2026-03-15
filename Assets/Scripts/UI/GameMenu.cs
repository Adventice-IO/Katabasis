using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class GameMenu : MonoBehaviour
{
    readonly Dictionary<string, Texture2D> languageIcons = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, Dictionary<string, string>> localeEntries = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<GameObject, string> languageByObject = new Dictionary<GameObject, string>();
    readonly Dictionary<XRSimpleInteractable, GameObject> objectByInteractable = new Dictionary<XRSimpleInteractable, GameObject>();
    readonly List<GameObject> langObjects = new List<GameObject>();

    MainController mainController;
    bool isActive;
    bool waitingForMenuData;
    string selectedLanguage = "";

    GameObject startBTObject;
    GameObject languagesContainer;
    UnityEngine.XR.Interaction.Toolkit.Interactors.IXRHoverInteractor currentHoverInteractor;
    GameObject hoveredObject;
    float hoveredSince;
    bool hoveredSelected;
    Vector3 hoveredOriginalScale = Vector3.one;

    public GameObject langBTPrefab;
    public float hoverSelectTime = 1f;
    public float buttonScaling = 0.2f;
    public float buttonSpacing = 1.2f;

    void OnEnable()
    {
        mainController = MainController.instance != null ? MainController.instance : FindAnyObjectByType<MainController>();
        CacheReferences();
    }

    void Update()
    {
        if (!isActive || !Application.isPlaying)
        {
            return;
        }

        for (int i = 0; i < langObjects.Count; i++)
        {
            GameObject langObject = langObjects[i];
            if (langObject == null)
            {
                continue;
            }

            langObject.transform.localPosition = new Vector3((i - (langObjects.Count / 2f) + 0.5f) * buttonSpacing, 0f, 0f);
            langObject.transform.LookAt(Camera.main.transform);
            langObject.transform.localScale = Vector3.one * buttonScaling;
        }

        UpdateHoveredObjectAnimation();


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
        mainController = MainController.instance != null ? MainController.instance : FindAnyObjectByType<MainController>();

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

        if (!DataManager.IsFolderReady(DataManager.DataFolder.Menu))
        {
            if (!waitingForMenuData)
            {
                waitingForMenuData = true;
                DataManager.PreloadFolder(DataManager.DataFolder.Menu, (success, path) =>
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
        LoadLanguageIcons();
        Debug.Log("GameMenu refresh - locale keys: " + localeEntries.Count + ", icons: " + languageIcons.Count, this);
        EnsureLanguageButtons();
        selectedLanguage = "";
        UpdateLanguageVisualState();
        UpdateStartButton();
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
        languages.Sort(StringComparer.OrdinalIgnoreCase);
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
            langObject.transform.localPosition = new Vector3(i * buttonSpacing, 0f, 0f);
            langObject.transform.localRotation = Quaternion.identity;

            RegisterLanguageButton(langObject, language);
            UpdateLanguageButtonContent(langObject, language);
            ResetObjectAnimation(langObject);
        }
    }

    List<string> GetAvailableLanguages()
    {
        HashSet<string> languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string language in languageIcons.Keys)
        {
            if (!string.IsNullOrWhiteSpace(language))
            {
                languages.Add(language);
            }
        }

        foreach (Dictionary<string, string> entry in localeEntries.Values)
        {
            if (entry == null)
            {
                continue;
            }

            foreach (string language in entry.Keys)
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
        Debug.Log("Registering language button for '" + language + "' on object: " + langObject.name, this);
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
        Texture2D icon = null;
        languageIcons.TryGetValue(language, out icon);

        MeshRenderer renderer = langObject.GetComponentInChildren<MeshRenderer>(true);
        if (renderer != null && renderer.material != null)
        {
            renderer.material.mainTexture = icon;
            renderer.material.color = Color.white;
        }

        TextMeshPro text = langObject.GetComponentInChildren<TextMeshPro>(true);
        if (text != null)
        {
            text.text = string.IsNullOrWhiteSpace(language) ? string.Empty : language.ToUpperInvariant();
            text.gameObject.SetActive(icon == null);
        }
    }

    void UpdateHoveredObjectAnimation()
    {
        if (hoveredObject == null)
        {
            return;
        }

        float hoverDuration = Time.time - hoveredSince;
        float normalizedHover = hoverSelectTime > 0f ? Mathf.Clamp01(hoverDuration / hoverSelectTime) : 1f;
        float pulse = 1f + Mathf.Sin(Time.time * 3f) * 0.03f;
        float scaleMultiplier = Mathf.Lerp(1f, 1.12f, normalizedHover) * pulse;
        hoveredObject.transform.localScale = hoveredOriginalScale * scaleMultiplier;

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

        targetObject.transform.localScale = Vector3.one;
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
        MeshRenderer renderer = targetObject != null ? targetObject.GetComponentInChildren<MeshRenderer>(true) : null;
        if (renderer != null && renderer.material != null)
        {
            renderer.material.color = selected ? Color.green : Color.white;
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

        TextMeshPro text = startBTObject.GetComponentInChildren<TextMeshPro>(true);
        if (text != null)
        {
            text.text = GetLocalizedText("start", selectedLanguage);
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
        if (mainController != null)
        {
            mainController.language = selectedLanguage;
            mainController.gameState = MainController.GameState.Intro;
        }

        setActive(false);
    }

    void PositionInFrontOfCamera()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            return;
        }

        transform.position = mainCamera.transform.TransformPoint(Vector3.forward * 5f);
        transform.LookAt(mainCamera.transform);
        transform.Rotate(0f, 180f, 0f);
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

    void LoadLocale()
    {
        localeEntries.Clear();

        string localePath = DataManager.GetFilePath(DataManager.DataFolder.Menu, "locale.json");
        if (string.IsNullOrWhiteSpace(localePath) || !File.Exists(localePath))
        {
            return;
        }

        try
        {
            Dictionary<string, Dictionary<string, string>> parsedEntries = DeserializeLocaleEntries(File.ReadAllText(localePath));
            if (parsedEntries == null)
            {
                return;
            }

            foreach (KeyValuePair<string, Dictionary<string, string>> entry in parsedEntries)
            {
                localeEntries[entry.Key] = entry.Value;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("Failed to load locale.json: " + ex.Message, this);
        }
    }

    void LoadLanguageIcons()
    {
        languageIcons.Clear();

        string menuPath = DataManager.GetBasePath(DataManager.DataFolder.Menu);
        if (string.IsNullOrWhiteSpace(menuPath) || !Directory.Exists(menuPath))
        {
            return;
        }

        string[] iconPaths = Directory.GetFiles(menuPath, "*.png", SearchOption.TopDirectoryOnly);
        for (int i = 0; i < iconPaths.Length; i++)
        {
            string language = Path.GetFileNameWithoutExtension(iconPaths[i]);
            Debug.Log("Found language icon file: " + iconPaths[i] + " for language: '" + language + "'", this);
            if (string.IsNullOrWhiteSpace(language))
            {
                continue;
            }

            try
            {
                byte[] imageBytes = File.ReadAllBytes(iconPaths[i]);
                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!texture.LoadImage(imageBytes))
                {
                    Destroy(texture);
                    continue;
                }
                Debug.Log("Loaded language icon for '" + language + "' from: " + iconPaths[i], this);
                texture.wrapMode = TextureWrapMode.Clamp;
                languageIcons[language] = texture;
            }
            catch (Exception ex)
            {
                Debug.LogError("Failed to load language icon '" + iconPaths[i] + "': " + ex.Message, this);
            }
        }
    }

    Dictionary<string, Dictionary<string, string>> DeserializeLocaleEntries(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            Type jsonSerializationType = Type.GetType("Unity.Serialization.Json.JsonSerialization, Unity.Serialization");
            if (jsonSerializationType == null)
            {
                return ParseLocaleJsonFallback(json);
            }

            System.Reflection.MethodInfo fromJsonMethod = jsonSerializationType.GetMethod("FromJson", new[] { typeof(string), Type.GetType("Unity.Serialization.Json.JsonSerializationParameters, Unity.Serialization") });
            if (fromJsonMethod == null)
            {
                foreach (System.Reflection.MethodInfo method in jsonSerializationType.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
                {
                    if (method.Name == "FromJson" && method.IsGenericMethodDefinition)
                    {
                        System.Reflection.ParameterInfo[] parameters = method.GetParameters();
                        if (parameters.Length > 0 && parameters[0].ParameterType == typeof(string))
                        {
                            fromJsonMethod = method;
                            break;
                        }
                    }
                }
            }

            if (fromJsonMethod != null)
            {
                object result;
                if (fromJsonMethod.IsGenericMethodDefinition)
                {
                    System.Reflection.MethodInfo genericMethod = fromJsonMethod.MakeGenericMethod(typeof(Dictionary<string, Dictionary<string, string>>));
                    object[] parameters = genericMethod.GetParameters().Length > 1
                        ? new object[] { json, Activator.CreateInstance(genericMethod.GetParameters()[1].ParameterType) }
                        : new object[] { json };
                    result = genericMethod.Invoke(null, parameters);
                }
                else
                {
                    result = fromJsonMethod.Invoke(null, new object[] { json, null });
                }

                if (result is Dictionary<string, Dictionary<string, string>> entries)
                {
                    return entries;
                }
            }
        }
        catch
        {
        }

        return ParseLocaleJsonFallback(json);
    }

    Dictionary<string, Dictionary<string, string>> ParseLocaleJsonFallback(string json)
    {
        Dictionary<string, Dictionary<string, string>> result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        int index = 0;

        SkipWhitespace(json, ref index);
        if (!TryConsume(json, ref index, '{'))
        {
            return result;
        }

        while (index < json.Length)
        {
            SkipWhitespace(json, ref index);
            if (TryConsume(json, ref index, '}'))
            {
                break;
            }

            string key = ReadString(json, ref index);
            if (string.IsNullOrWhiteSpace(key))
            {
                break;
            }

            if (!TryConsume(json, ref index, ':'))
            {
                break;
            }

            result[key] = ReadStringMap(json, ref index);

            SkipWhitespace(json, ref index);
            if (!TryConsume(json, ref index, ','))
            {
                TryConsume(json, ref index, '}');
                break;
            }
        }

        return result;
    }

    Dictionary<string, string> ReadStringMap(string json, ref int index)
    {
        Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        SkipWhitespace(json, ref index);
        if (!TryConsume(json, ref index, '{'))
        {
            return result;
        }

        while (index < json.Length)
        {
            SkipWhitespace(json, ref index);
            if (TryConsume(json, ref index, '}'))
            {
                break;
            }

            string key = ReadString(json, ref index);
            if (string.IsNullOrWhiteSpace(key))
            {
                break;
            }

            if (!TryConsume(json, ref index, ':'))
            {
                break;
            }

            string value = ReadString(json, ref index);
            result[key] = value ?? string.Empty;

            SkipWhitespace(json, ref index);
            if (!TryConsume(json, ref index, ','))
            {
                TryConsume(json, ref index, '}');
                break;
            }
        }

        return result;
    }

    string ReadString(string json, ref int index)
    {
        SkipWhitespace(json, ref index);
        if (!TryConsume(json, ref index, '"'))
        {
            return null;
        }

        System.Text.StringBuilder builder = new System.Text.StringBuilder();
        while (index < json.Length)
        {
            char c = json[index++];
            if (c == '"')
            {
                return builder.ToString();
            }

            if (c == '\\' && index < json.Length)
            {
                char escaped = json[index++];
                switch (escaped)
                {
                    case '"': builder.Append('"'); break;
                    case '\\': builder.Append('\\'); break;
                    case '/': builder.Append('/'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    default: builder.Append(escaped); break;
                }
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    void SkipWhitespace(string json, ref int index)
    {
        while (index < json.Length && char.IsWhiteSpace(json[index]))
        {
            index++;
        }
    }

    bool TryConsume(string json, ref int index, char character)
    {
        SkipWhitespace(json, ref index);
        if (index < json.Length && json[index] == character)
        {
            index++;
            return true;
        }

        return false;
    }

    public void GazeHover(HoverEnterEventArgs args)
    {
    }

    public void GazeExit(HoverExitEventArgs args)
    {
    }
}
