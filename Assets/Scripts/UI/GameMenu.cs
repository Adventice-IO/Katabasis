using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

public class GameMenu : MonoBehaviour
{
    UIDocument uiDocument;
    VisualElement languagesContainer;
    Button startButton;

    readonly Dictionary<string, Texture2D> languageIcons = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, Dictionary<string, string>> localeEntries = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

    MainController mainController;
    bool isActive;
    bool menuInitialized;
    bool waitingForMenuData;
    string selectedLanguage = "en";

    void OnEnable()
    {
        uiDocument = GetComponent<UIDocument>();
        mainController = MainController.instance != null ? MainController.instance : FindAnyObjectByType<MainController>();
        SetupDocument();

        if (uiDocument != null)
        {
            uiDocument.enabled = false;
        }
    }

    void Update()
    {
        if (!isActive || !Application.isPlaying)
        {
            return;
        }

        if (!menuInitialized)
        {
            SetupDocument();
            if (menuInitialized)
            {
                LoadLocale();
                LoadLanguageIcons();
                BuildLanguageButtons();
            }
        }

        PositionInFrontOfCamera();

    }

    public void setActive(bool active)
    {
        isActive = active;
        mainController = MainController.instance != null ? MainController.instance : FindAnyObjectByType<MainController>();

        if (uiDocument == null)
        {
            uiDocument = GetComponent<UIDocument>();
        }

        SetupDocument();

        if (uiDocument != null)
        {
            uiDocument.enabled = active;
        }

        Collider collider = GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = active;
        }

        if (!active)
        {
            return;
        }

        PositionInFrontOfCamera();
        SelectLanguage("en");
        if (startButton != null)
        {
            startButton.style.display = DisplayStyle.None;
        }

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
                        LoadLocale();
                        LoadLanguageIcons();
                        BuildLanguageButtons();
                    }
                });
            }
            return;
        }

        LoadLocale();
        LoadLanguageIcons();
        BuildLanguageButtons();
    }

    void SetupDocument()
    {
        if (menuInitialized || uiDocument == null || uiDocument.rootVisualElement == null)
        {
            return;
        }

        languagesContainer = uiDocument.rootVisualElement.Q<VisualElement>("languages");
        startButton = uiDocument.rootVisualElement.Q<Button>("startbt");

        if (startButton != null)
        {
            startButton.clicked -= OnStartClicked;
            startButton.clicked += OnStartClicked;
        }

        LoadLocale();
        LoadLanguageIcons();
        menuInitialized = true;
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

                texture.wrapMode = TextureWrapMode.Clamp;
                languageIcons[language] = texture;
            }
            catch (Exception ex)
            {
                Debug.LogError("Failed to load language icon '" + iconPaths[i] + "': " + ex.Message, this);
            }
        }
    }

    void BuildLanguageButtons()
    {
        if (languagesContainer == null)
        {
            return;
        }

        languagesContainer.Clear();

        List<string> languages = new List<string>(languageIcons.Keys);
        languages.Sort(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < languages.Count; i++)
        {
            string language = languages[i];
            Button languageButton = new Button(() => OnLanguageClicked(language));
            languageButton.name = language;
            languageButton.tooltip = language;

            languageButton.text = string.Empty;
            languageButton.style.width = 96;
            languageButton.style.height = 64;
            languageButton.style.paddingLeft = 0;
            languageButton.style.paddingRight = 0;
            languageButton.style.paddingTop = 0;
            languageButton.style.paddingBottom = 0;
            languageButton.style.alignItems = Align.Center;
            languageButton.style.justifyContent = Justify.Center;

            Texture2D icon = languageIcons[language];
            if (icon != null)
            {
                Image image = new Image();
                image.image = icon;
                image.scaleMode = ScaleMode.ScaleToFit;
                image.pickingMode = PickingMode.Ignore;
                image.style.width = Length.Percent(100);
                image.style.height = Length.Percent(100);
                languageButton.Add(image);
            }
            else
            {
                languageButton.text = language;
            }

            languagesContainer.Add(languageButton);
        }
    }

    void OnLanguageClicked(string language)
    {
        SelectLanguage(language);
        UpdateStartButton();
    }

    void SelectLanguage(string language)
    {
        selectedLanguage = string.IsNullOrWhiteSpace(language) ? "en" : language;

        if (mainController != null)
        {
            mainController.language = selectedLanguage;
        }
    }

    void UpdateStartButton()
    {
        if (startButton == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(selectedLanguage))
        {
            startButton.style.display = DisplayStyle.None;
            return;
        }

        startButton.style.display = DisplayStyle.Flex;
        startButton.text = GetLocalizedText("start", selectedLanguage);
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
}
