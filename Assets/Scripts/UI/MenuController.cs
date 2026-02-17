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

    private void OnEnable()
    {
        // Safety check: ensure we have the document reference
        SetupMenu();

        if (Application.isPlaying && menuButtonAction.action != null)
        {
            menuButtonAction.action.Enable();
            menuButtonAction.action.performed += OnMenuButtonPressed;
        }

        if (!enabledAtStart) uiDocument.enabled = false;
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
        uiDocument.enabled = !uiDocument.enabled;
        GetComponent<BoxCollider>().enabled = uiDocument.enabled;
        if (uiDocument.enabled)
        {
            SetupMenu();

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
            return button;
        };

        tunnelsList.makeItem = () =>
        {
            var button = new Button();
            return button;
        };

        sallesList.bindItem = (element, index) =>
        {
            var button = element as Button;
            button.clicked -= () => OnSalleClicked(index);
            button.clicked += () => OnSalleClicked(index);
            button.text = sallesItems[index].gameObject.name;
        };

        tunnelsList.bindItem = (element, index) =>
        {
            var button = element as Button;
            button.clicked -= () => OnTunnelClicked(index);
            button.clicked += () => OnTunnelClicked(index);

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
            if( editModeButton != null)
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
        mainController.TeleportToSalle(salle);
    }

    private void OnTunnelClicked(int index)
    {
        var tunnel = tunnelsList.itemsSource[index] as Tunnel;
        mainController.salle = null;
        mainController.tunnel = tunnel;
        mainController.ResetPosition(true);

        tunnelsList.SetSelectionWithoutNotify(new List<int> { });
    }

}
