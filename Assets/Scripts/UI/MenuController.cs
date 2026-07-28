using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using static UnityEngine.Analytics.IAnalytic;


public class MenuController : MonoBehaviour
{
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private InputActionProperty menuButtonAction;


    MainController mainController;


    public bool enabledAtStart = false;

    public Transform salles;
    public Transform tunnels;

    ListView sallesList;
    ListView tunnelsList;

    Button lockOnTrackButton;
    Button editModeButton;
    Button playpauseButton;
    bool menuVisible;

    private void OnEnable()
    {
        uiDocument = GetComponent<UIDocument>();
        if (uiDocument == null)
        {
            Debug.LogError("MenuController requires a UIDocument on the same GameObject.", this);
            enabled = false;
            return;
        }

        // Keep this document registered with UI Toolkit. Disabling a UIDocument
        // here can tear down another runtime panel that is being initialized in
        // the same frame (for example SettingsMenu).
        uiDocument.enabled = true;
        SetMenuVisible(enabledAtStart);
        if (menuVisible)
        {
            SetupMenu();
        }

        if (Application.isPlaying && menuButtonAction.action != null)
        {
            menuButtonAction.action.Enable();
            menuButtonAction.action.performed += OnMenuButtonPressed;
        }
    }

    private void OnDisable()
    {
        if (Application.isPlaying && menuButtonAction.action != null)
        {
            menuButtonAction.action.performed -= OnMenuButtonPressed;
            menuButtonAction.action.Disable();
        }
    }

    private void OnMenuButtonPressed(InputAction.CallbackContext obj)
    {
        if (menuVisible)
        {
            SetMenuVisible(false);
            return;
        }

        SetupMenu();
        SetMenuVisible(true);
    }

    private void SetMenuVisible(bool visible)
    {
        menuVisible = visible;

        if (uiDocument != null)
        {
            // Toggle only this document's content. The UIDocument itself stays
            // enabled so it cannot disturb SettingsMenu's independent panel.
            VisualElement root = uiDocument.rootVisualElement;
            if (root != null)
            {
                root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        BoxCollider panelCollider = GetComponent<BoxCollider>();
        if (panelCollider != null)
        {
            panelCollider.enabled = visible;
        }
    }

    // 3. Called when you change values in the Inspector (useful for live updates)
    private void OnValidate()
    {
        if (Application.isPlaying) return; // Skip in Play Mode
        SetupMenu();
    }

    private void SetupMenu()
    {
        if (Application.isPlaying)
        {
            transform.position = Camera.main.transform.TransformPoint(Vector3.forward * 2);
            transform.LookAt(Camera.main.transform);
            transform.Rotate(0, 180, 0);
        }else
        {
            return;
        }

        uiDocument = GetComponent<UIDocument>();
        mainController = FindAnyObjectByType<MainController>();

        if (salles == null)
        {
            var salleParent = GameObject.Find("Salles");
            if (salleParent != null)
            {
                salles = salleParent.transform;
            }
        }

        if (tunnels == null)
        {
            var tunnelParent = GameObject.Find("Tunnels");
            if (tunnelParent != null)
            {
                tunnels = tunnelParent.transform;
            }
        }

        if (uiDocument == null || mainController == null || salles == null || tunnels == null) return;
        var root = uiDocument.rootVisualElement;
        if (root == null) return;



        sallesList = root.Q<ListView>("salleslist");
        tunnelsList = root.Q<ListView>("tunnelslist");

        List<Salle> sallesItems = salles.GetComponentsInChildren<Salle>().ToList();
        List<Tunnel> tunnelsItems = tunnels.GetComponentsInChildren<Tunnel>().ToList();
        sallesList.itemsSource = sallesItems;
        tunnelsList.itemsSource = tunnelsItems;

        sallesList.makeItem = () =>
        {
            var button = new Button();
            button.clicked += () => OnSalleButtonClicked(button);
            return button;
        };

        tunnelsList.makeItem = () =>
        {
            var button = new Button();
            button.clicked += () => OnTunnelButtonClicked(button);
            return button;
        };

        sallesList.bindItem = (element, index) =>
        {
            var button = element as Button;
            button.userData = index;
            button.text = sallesItems[index].gameObject.name;
        };

        tunnelsList.bindItem = (element, index) =>
        {
            var button = element as Button;
            button.userData = index;

            button.text = tunnelsItems[index].gameObject.name;
        };

        sallesList.Rebuild();
        tunnelsList.Rebuild();


        lockOnTrackButton = root.Q<Button>("freemotionbt");
        lockOnTrackButton.clicked -= FreeMotionButton_clicked;
        lockOnTrackButton.clicked += FreeMotionButton_clicked;
        editModeButton = root.Q<Button>("editmodebt");
        editModeButton.clicked -= EditModeButton_clicked;
        editModeButton.clicked += EditModeButton_clicked;

        playpauseButton = root.Q<Button>("playpause");
        playpauseButton.clicked -= onPlayPauseClicked;
        playpauseButton.clicked += onPlayPauseClicked;


    }

    private void Update()
    {
        if (Application.isPlaying)
        {
            if (lockOnTrackButton != null)
            {
                if (mainController.freeMotion) lockOnTrackButton.RemoveFromClassList("active");
                else lockOnTrackButton.AddToClassList("active");
            }
            if (editModeButton != null)
            {
                if (mainController.editMode) editModeButton.AddToClassList("active");
                else editModeButton.RemoveFromClassList("active");
            }
        }
    }

    private void FreeMotionButton_clicked()
    {
        mainController.freeMotion = !mainController.freeMotion;
        if (mainController.freeMotion) lockOnTrackButton.RemoveFromClassList("active");
        else lockOnTrackButton.AddToClassList("active");
    }

    private void EditModeButton_clicked()
    {
        mainController.editMode = !mainController.editMode;
        if (mainController.editMode) editModeButton.AddToClassList("active");
        else editModeButton.RemoveFromClassList("active");
    }

    private void OnSalleClicked(int index)
    {
        var salle = sallesList.itemsSource[index] as Salle;
        
        Debug.Log("Salle button clicked: " + index+" > "+salle.name);
        mainController.TeleportToSalle(salle);
    }

    private void OnSalleButtonClicked(Button button)
    {
        if (button.userData is int index)
        {
            OnSalleClicked(index);
        }
    }

    private void OnTunnelClicked(int index)
    {
        var tunnel = tunnelsList.itemsSource[index] as Tunnel;
        mainController.salle = null;
        mainController.tunnel = tunnel;
        mainController.ResetPosition(true);

        tunnelsList.SetSelectionWithoutNotify(new List<int> { });
    }

    private void OnTunnelButtonClicked(Button button)
    {
        if (button.userData is int index)
        {
            OnTunnelClicked(index);
        }
    }

    private void onPlayPauseClicked()
    {
        if (mainController.isRunning)
        {
            mainController.Pause();
        }
        else
        {
            mainController.Play();
        }

    }

}
