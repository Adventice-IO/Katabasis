using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MainController))]
public class MainControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        MainController script = (MainController)target;

        // Draw the standard default inspector (variables, sliders)
        DrawDefaultInspector();

        GUILayout.Space(15);

        // Define a style for the Big Buttons
        GUIStyle bigButtonStyle = new GUIStyle(GUI.skin.button);
        bigButtonStyle.fontSize = 12;
        bigButtonStyle.fontStyle = FontStyle.Bold;
        bigButtonStyle.fixedHeight = 20;

        // --- RUNTIME CONTROLS ---
        //if (Application.isPlaying)
        //{
        GUILayout.BeginHorizontal();

        if (GUILayout.Button("Reset to Start", bigButtonStyle))
        {
            script.Reset();
            // Force update so the Scene View refreshes immediately
            EditorUtility.SetDirty(script);
        }

        if (GUILayout.Button("GO / RESUME", bigButtonStyle))
        {
            script.Play();
        }

        if (GUILayout.Button("PAUSE", bigButtonStyle))
        {
            script.Pause();
        }

        GUILayout.EndHorizontal();

        // --- NAVIGATION ---
        GUILayout.Space(10);
        EditorGUILayout.LabelField("Navigation", EditorStyles.boldLabel);

        if (script.salle == null)
        {
            EditorGUILayout.HelpBox("Set the current `salle` to enable destination list (based on out tunnels).", MessageType.Info);
        }
        else
        {
            var outTunnels = script.getAllOutTunnels();
            if (outTunnels == null || outTunnels.Count == 0)
            {
                EditorGUILayout.HelpBox("No outgoing tunnels found for this salle.", MessageType.Info);
            }
            else
            {
                // Build unique destination list
                var destinations = new System.Collections.Generic.List<Salle>();
                foreach (var t in outTunnels)
                {
                    if (t == null) continue;
                    var dest = t.salleArrivee;
                    if (dest == null) continue;
                    if (!destinations.Contains(dest)) destinations.Add(dest);
                }

                if (destinations.Count == 0)
                {
                    EditorGUILayout.HelpBox("Outgoing tunnels exist but have no `salleArrivee`.", MessageType.Warning);
                }
                else
                {
                    EditorGUILayout.LabelField("Destination", EditorStyles.miniBoldLabel);

                    var names = new string[destinations.Count];
                    for (int i = 0; i < destinations.Count; i++)
                    {
                        names[i] = destinations[i] != null ? destinations[i].name : "(null)";
                    }

                    // Persist selection per-editor-session
                    int selectedIndex = EditorPrefs.GetInt("MainControllerEditor.SelectedSalleIndex", 0);
                    selectedIndex = Mathf.Clamp(selectedIndex, 0, destinations.Count - 1);
                    int newIndex = EditorGUILayout.Popup(selectedIndex, names);
                    if (newIndex != selectedIndex)
                    {
                        selectedIndex = newIndex;
                        EditorPrefs.SetInt("MainControllerEditor.SelectedSalleIndex", selectedIndex);
                    }

                    var selectedSalle = destinations[selectedIndex];
                    using (new GUILayout.HorizontalScope())
                    {
                        GUI.enabled = selectedSalle != null;
                        if (GUILayout.Button("Go", GUILayout.Width(80)))
                        {
                            Undo.RecordObject(script, "Go To Salle");
                            script.GoToSalle(selectedSalle);
                            EditorUtility.SetDirty(script);
                        }
                        if (GUILayout.Button("Teleport", GUILayout.Width(80)))
                        {
                            Undo.RecordObject(script, "Teleport To Salle");
                            script.TeleportToSalle(selectedSalle);
                            EditorUtility.SetDirty(script);
                        }
                        GUI.enabled = true;
                    }
                }
            }
        }
    }
}