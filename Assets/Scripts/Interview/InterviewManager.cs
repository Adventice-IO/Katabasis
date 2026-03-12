using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

[ExecuteAlways]
public class InterviewManager : MonoBehaviour
{
    [Serializable]
    public struct RoomPersonAssignment
    {
        public Salle salle;
        public Interview interviewSlot;
        public string person;
    }

    public struct InterviewData
    {
        public string person;
        public string filename;
        public List<string> themes;
        public bool isIntro;
        public bool visited;
        public bool proposed;
        public string depthkitId;
        public int level;
        public int note;
    }

    public struct PersonInterviewStats
    {
        public string person;
        public InterviewData introInterview;
        public List<InterviewData> interviews;
    }

    readonly List<InterviewData> interviewDataList = new List<InterviewData>();
    readonly List<PersonInterviewStats> personStatsList = new List<PersonInterviewStats>();
    readonly List<RoomPersonAssignment> roomAssignments = new List<RoomPersonAssignment>();
    readonly Dictionary<string, HashSet<int>> playedLevelsByPerson = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> playedPersonsSinceAssignment = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    System.Random assignmentRandom;

    public bool generateAssignment;
    public bool simulateGameplay;
    [Range(0f, 1f)]
    public float diversity = 1f;
    [Range(0f, 1f)]
    public float levelUpBias = 0f;
    [Range(0f, 1f)]
    public float simulatedCuriosity = 0.5f;

    void OnEnable()
    {
        LoadInterviewData();
        AssignPersonsToRooms();
        ApplyAssignmentsToScene();
        logAssignments();
    }

    void Update()
    {
        if (generateAssignment)
        {
            generateAssignment = false;
            AssignPersonsToRooms();
            ApplyAssignmentsToScene();
            logAssignments();
        }

        if (simulateGameplay)
        {
            simulateGameplay = false;
            SimulateGameplay();
        }
    }

    public void ResetGame()
    {
        for (int i = 0; i < interviewDataList.Count; i++)
        {
            var data = interviewDataList[i];
            data.visited = false;
            data.proposed = false;
            interviewDataList[i] = data;
        }

        playedLevelsByPerson.Clear();
        playedPersonsSinceAssignment.Clear();
        RebuildPersonStats();
        AssignPersonsToRooms();
        ApplyAssignmentsToScene();
    }

    public void ClearVisits()
    {
        ResetGame();
    }

    public bool MarkInterviewVisited(string interviewId)
    {
        bool found = false;
        InterviewData? matchedInterview = null;

        for (int i = 0; i < interviewDataList.Count; i++)
        {
            bool isMatch =
                string.Equals(interviewDataList[i].filename, interviewId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(interviewDataList[i].depthkitId, interviewId, StringComparison.OrdinalIgnoreCase);

            if (!isMatch)
            {
                continue;
            }

            var data = interviewDataList[i];
            data.visited = true;
            data.proposed = true;
            interviewDataList[i] = data;
            matchedInterview = data;
            found = true;
            break;
        }

        if (!found || !matchedInterview.HasValue)
        {
            return false;
        }

        RegisterPlayedInterview(matchedInterview.Value);
        RebuildPersonStats();
        ApplyAssignmentsToScene();
        return true;
    }

    public void AssignPersonsToRooms(int? seed = null)
    {
        if (seed.HasValue)
        {
            assignmentRandom = new System.Random(seed.Value);
        }
        else if (assignmentRandom == null)
        {
            assignmentRandom = new System.Random(Environment.TickCount);
        }

        roomAssignments.Clear();
        playedLevelsByPerson.Clear();
        playedPersonsSinceAssignment.Clear();

        Salle[] salles = FindObjectsByType<Salle>(FindObjectsSortMode.None)
            .Where(salle => salle != null && !salle.isExit)
            .ToArray();

        Tunnel[] tunnels = FindObjectsByType<Tunnel>(FindObjectsSortMode.None);
        Dictionary<Salle, int> connectivity = BuildConnectivityMap(salles, tunnels);
        Dictionary<Salle, int> depthMap = BuildForwardDepthMap(salles, tunnels);

        List<Salle> orderedSalles = salles
            .OrderBy(salle => depthMap.ContainsKey(salle) ? depthMap[salle] : int.MaxValue)
            .ThenByDescending(salle => connectivity.ContainsKey(salle) ? connectivity[salle] : 0)
            .ThenBy(_ => assignmentRandom.Next())
            .ToList();

        List<string> allPersons = personStatsList
            .Select(stats => stats.person)
            .OrderBy(_ => assignmentRandom.Next())
            .ToList();

        int maxSlotsInOneRoom = orderedSalles
            .Select(salle => salle != null ? salle.interviews.Length : 0)
            .DefaultIfEmpty(0)
            .Max();

        int targetUniquePeople = Mathf.Clamp(
            Mathf.RoundToInt(Mathf.Lerp(maxSlotsInOneRoom, allPersons.Count, diversity)),
            Mathf.Min(maxSlotsInOneRoom, allPersons.Count),
            allPersons.Count);

        List<string> availablePersons = allPersons.Take(targetUniquePeople).ToList();

        for (int i = 0; i < orderedSalles.Count; i++)
        {
            Salle salle = orderedSalles[i];
            Interview[] slots = salle.interviews;
            List<string> roomAvailablePersons = availablePersons.OrderBy(_ => assignmentRandom.Next()).ToList();

            for (int j = 0; j < slots.Length; j++)
            {
                if (roomAvailablePersons.Count == 0)
                {
                    break;
                }

                string person = roomAvailablePersons[0];
                roomAvailablePersons.RemoveAt(0);

                roomAssignments.Add(new RoomPersonAssignment
                {
                    salle = salle,
                    interviewSlot = slots[j],
                    person = person
                });
            }
        }
    }

    public void ApplyAssignmentsToScene()
    {
        for (int i = 0; i < roomAssignments.Count; i++)
        {
            RoomPersonAssignment assignment = roomAssignments[i];
            if (assignment.interviewSlot == null || string.IsNullOrWhiteSpace(assignment.person))
            {
                continue;
            }

            InterviewData[] interviewsToShow = GetInterviewsToPlayForPerson(assignment.person);
            InterviewData selectedInterview = interviewsToShow.FirstOrDefault(data => !data.isIntro);
            if (string.IsNullOrWhiteSpace(selectedInterview.depthkitId) && interviewsToShow.Length > 0)
            {
                selectedInterview = interviewsToShow[interviewsToShow.Length - 1];
            }

            if (!string.IsNullOrWhiteSpace(selectedInterview.depthkitId))
            {
                assignment.interviewSlot.set(selectedInterview.depthkitId, selectedInterview.level);
            }
        }
    }

    public InterviewData[] GetInterviewsToPlayForPerson(string person)
    {
        return GetInterviewsToPlayForPerson(person, playedPersonsSinceAssignment, playedLevelsByPerson);
    }

    InterviewData[] GetInterviewsToPlayForPerson(string person, HashSet<string> playedPersons, Dictionary<string, HashSet<int>> playedLevels)
    {
        if (string.IsNullOrWhiteSpace(person))
        {
            return Array.Empty<InterviewData>();
        }

        PersonInterviewStats? stats = GetPersonStats(person);
        if (!stats.HasValue)
        {
            return Array.Empty<InterviewData>();
        }

        List<InterviewData> result = new List<InterviewData>();
        bool hasPlayedBefore = playedPersons.Contains(person);

        if (!hasPlayedBefore && !string.IsNullOrEmpty(stats.Value.introInterview.filename))
        {
            result.Add(stats.Value.introInterview);
        }

        InterviewData? bestInterview = GetBestInterviewForPerson(stats.Value.person, playedLevels);
        if (bestInterview.HasValue)
        {
            result.Add(bestInterview.Value);
        }

        return result.ToArray();
    }

    public InterviewData? GetBestInterviewForPerson(string person)
    {
        return GetBestInterviewForPerson(person, playedLevelsByPerson);
    }

    InterviewData? GetBestInterviewForPerson(string person, Dictionary<string, HashSet<int>> playedLevels)
    {
        PersonInterviewStats? stats = GetPersonStats(person);
        if (!stats.HasValue)
        {
            return null;
        }

        List<InterviewData> candidates = GetPlayableCandidates(stats.Value.person, stats.Value.interviews, playedLevels);
        if (candidates.Count == 0)
        {
            return null;
        }

        List<InterviewData> leveledCandidates = ApplyLevelBias(candidates);

        int bestNote = leveledCandidates.Max(data => data.note);
        List<InterviewData> bestNoteCandidates = leveledCandidates
            .Where(data => data.note == bestNote)
            .ToList();

        int randomIndex = assignmentRandom != null ? assignmentRandom.Next(bestNoteCandidates.Count) : UnityEngine.Random.Range(0, bestNoteCandidates.Count);
        return bestNoteCandidates[randomIndex];
    }

    public InterviewData[] GetNextInterviewsForPerson(string person)
    {
        return GetInterviewsToPlayForPerson(person);
    }

    public InterviewData[] GetNextInterviewsForTheme(string theme)
    {
        if (string.IsNullOrWhiteSpace(theme))
        {
            return Array.Empty<InterviewData>();
        }

        List<InterviewData> candidates = interviewDataList
            .Where(data => !data.isIntro)
            .Where(data => data.themes.Any(t => string.Equals(t, theme, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        List<InterviewData> playable = new List<InterviewData>();
        for (int i = 0; i < candidates.Count; i++)
        {
            if (IsInterviewAllowedForPlayback(candidates[i], playedLevelsByPerson))
            {
                playable.Add(candidates[i]);
            }
        }

        if (playable.Count == 0)
        {
            return Array.Empty<InterviewData>();
        }

        List<InterviewData> leveledCandidates = ApplyLevelBias(playable);

        int bestNote = leveledCandidates.Max(data => data.note);
        List<InterviewData> bestNoteCandidates = leveledCandidates
            .Where(data => data.note == bestNote)
            .ToList();

        int randomIndex = assignmentRandom != null ? assignmentRandom.Next(bestNoteCandidates.Count) : UnityEngine.Random.Range(0, bestNoteCandidates.Count);
        InterviewData picked = bestNoteCandidates[randomIndex];

        List<InterviewData> result = new List<InterviewData>();
        if (!playedPersonsSinceAssignment.Contains(picked.person))
        {
            InterviewData? intro = GetIntroForPerson(picked.person);
            if (intro.HasValue)
            {
                result.Add(intro.Value);
            }
        }

        result.Add(picked);
        return result.ToArray();
    }

    public bool HasIntroBeenPlayed(string person)
    {
        PersonInterviewStats? stats = GetPersonStats(person);
        if (!stats.HasValue)
        {
            return false;
        }

        return !string.IsNullOrEmpty(stats.Value.introInterview.filename) && stats.Value.introInterview.visited;
    }

    public bool HasIntroBeenPlayed(InterviewData data)
    {
        return HasIntroBeenPlayed(data.person);
    }

    public InterviewData? GetIntroForPerson(string person)
    {
        PersonInterviewStats? stats = GetPersonStats(person);
        if (!stats.HasValue)
        {
            return null;
        }

        if (string.IsNullOrEmpty(stats.Value.introInterview.filename))
        {
            return null;
        }

        return stats.Value.introInterview;
    }

    void LoadInterviewData()
    {
        interviewDataList.Clear();
        personStatsList.Clear();

        string csvPath = Path.Combine(Application.streamingAssetsPath, "interviews.csv");
        if (!File.Exists(csvPath))
        {
            Debug.LogWarning("Interview CSV not found at " + csvPath);
            return;
        }

        string[] lines = File.ReadAllLines(csvPath);
        if (lines.Length <= 1)
        {
            return;
        }

        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string[] fields = SplitCsvLine(line);
            if (IsEmptyRow(fields))
            {
                continue;
            }

            InterviewData data = new InterviewData
            {
                filename = GetField(fields, 0),
                depthkitId = GetField(fields, 1),
                level = ParseInt(GetField(fields, 2), 1),
                note = ParseInt(GetField(fields, 3), 1),
                person = GetField(fields, 4),
                themes = GetField(fields, 5).Split(',').Select(t => t.Trim()).Where(t => !string.IsNullOrEmpty(t)).ToList(),
                visited = false,
                proposed = false
            };

            data.isIntro = data.themes.Any(t => string.Equals(t, "Intro", StringComparison.OrdinalIgnoreCase));
            if (data.level <= 0)
            {
                data.level = 1;
            }
            interviewDataList.Add(data);
        }

        RebuildPersonStats();
    }

    void RebuildPersonStats()
    {
        personStatsList.Clear();

        foreach (var group in interviewDataList.GroupBy(data => data.person))
        {
            PersonInterviewStats stats = new PersonInterviewStats
            {
                person = group.Key,
                introInterview = default,
                interviews = group
                    .Where(data => !data.isIntro)
                    .OrderBy(data => data.level)
                    .ThenByDescending(data => data.note)
                    .ThenBy(data => data.filename)
                    .ToList()
            };

            foreach (var interview in group)
            {
                if (interview.isIntro)
                {
                    stats.introInterview = interview;
                    break;
                }
            }

            personStatsList.Add(stats);
        }
    }

    List<InterviewData> GetPlayableCandidates(string person, List<InterviewData> interviews)
    {
        return GetPlayableCandidates(person, interviews, playedLevelsByPerson);
    }

    List<InterviewData> GetPlayableCandidates(string person, List<InterviewData> interviews, Dictionary<string, HashSet<int>> playedLevels)
    {
        int highestPlayedLevel = GetHighestPlayedLevel(person, playedLevels);
        int maxAllowedLevel = Mathf.Max(1, highestPlayedLevel + 1);
        int minAllowedLevel = highestPlayedLevel <= 0 ? 1 : highestPlayedLevel;

        return interviews
            .Where(data => !data.visited)
            .Where(data => data.level >= minAllowedLevel && data.level <= maxAllowedLevel)
            .ToList();
    }

    List<InterviewData> ApplyLevelBias(List<InterviewData> candidates)
    {
        if (candidates == null || candidates.Count <= 1)
        {
            return candidates ?? new List<InterviewData>();
        }

        List<int> availableLevels = candidates
            .Select(data => data.level)
            .Distinct()
            .OrderBy(level => level)
            .ToList();

        if (availableLevels.Count <= 1)
        {
            return candidates;
        }

        int chosenLevel;
        double roll = assignmentRandom != null ? assignmentRandom.NextDouble() : UnityEngine.Random.value;
        if (roll < levelUpBias)
        {
            chosenLevel = availableLevels[availableLevels.Count - 1];
        }
        else
        {
            int levelIndex = assignmentRandom != null ? assignmentRandom.Next(availableLevels.Count) : UnityEngine.Random.Range(0, availableLevels.Count);
            chosenLevel = availableLevels[levelIndex];
        }

        return candidates.Where(data => data.level == chosenLevel).ToList();
    }

    int GetHighestPlayedLevel(string person)
    {
        return GetHighestPlayedLevel(person, playedLevelsByPerson);
    }

    int GetHighestPlayedLevel(string person, Dictionary<string, HashSet<int>> playedLevels)
    {
        if (!playedLevels.TryGetValue(person, out HashSet<int> levels) || levels.Count == 0)
        {
            return 0;
        }

        return levels.Max();
    }

    bool IsInterviewAllowedForPlayback(InterviewData candidate)
    {
        return IsInterviewAllowedForPlayback(candidate, playedLevelsByPerson);
    }

    bool IsInterviewAllowedForPlayback(InterviewData candidate, Dictionary<string, HashSet<int>> playedLevels)
    {
        int highestPlayedLevel = GetHighestPlayedLevel(candidate.person, playedLevels);
        int maxAllowedLevel = Mathf.Max(1, highestPlayedLevel + 1);
        int minAllowedLevel = highestPlayedLevel <= 0 ? 1 : highestPlayedLevel;
        return !candidate.visited && candidate.level >= minAllowedLevel && candidate.level <= maxAllowedLevel;
    }

    void RegisterPlayedInterview(InterviewData interview)
    {
        playedPersonsSinceAssignment.Add(interview.person);

        if (!playedLevelsByPerson.TryGetValue(interview.person, out HashSet<int> levels))
        {
            levels = new HashSet<int>();
            playedLevelsByPerson[interview.person] = levels;
        }

        if (!interview.isIntro)
        {
            levels.Add(interview.level);
        }
    }

    public void SimulateGameplay()
    {
        if (assignmentRandom == null)
        {
            assignmentRandom = new System.Random(Environment.TickCount);
        }

        Salle startSalle = FindObjectsByType<Salle>(FindObjectsSortMode.None)
            .FirstOrDefault(salle => string.Equals(salle.name, "A", StringComparison.OrdinalIgnoreCase));

        if (startSalle == null)
        {
            Debug.LogWarning("<color=#ff5555>Simulation aborted:</color> no salle named <color=#ffff55>A</color> found.");
            return;
        }

        HashSet<string> simulatedPlayedPersons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, HashSet<int>> simulatedPlayedLevels = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
        HashSet<Salle> visitedSalles = new HashSet<Salle>();

        Salle currentSalle = startSalle;
        int stepIndex = 1;
        int safety = 0;
        int maxSteps = Mathf.Max(10, roomAssignments.Count * 3);

        Debug.Log("<b><color=#55ff55>=== GAMEPLAY SIMULATION START ===</color></b>");
        Debug.Log($"<color=#55ffff>Start salle:</color> <b><color=#ffff55>{currentSalle.name}</color></b>");

        while (currentSalle != null && safety < maxSteps)
        {
            visitedSalles.Add(currentSalle);
            List<RoomPersonAssignment> roomPeople = roomAssignments
                .Where(assignment => assignment.salle == currentSalle && !string.IsNullOrWhiteSpace(assignment.person))
                .ToList();

            Debug.Log($"<b><color=#ffaa55>STEP {stepIndex}</color></b> — Room <b><color=#ffff55>{currentSalle.name}</color></b>");

            if (roomPeople.Count == 0)
            {
                Debug.Log("  <color=#ff7777>No assigned persons in this room.</color>");
            }
            else
            {
                int playCount = GetSimulatedPlayCount(roomPeople.Count);
                List<RoomPersonAssignment> shuffledPeople = roomPeople.OrderBy(_ => assignmentRandom.Next()).ToList();

                Debug.Log($"  <color=#aaaaaa>Will play</color> <b><color=#ffffff>{playCount}</color></b> <color=#aaaaaa>interview(s)</color>");

                for (int i = 0; i < playCount; i++)
                {
                    RoomPersonAssignment assignment = shuffledPeople[i];
                    InterviewData[] toPlay = GetInterviewsToPlayForPerson(assignment.person, simulatedPlayedPersons, simulatedPlayedLevels);

                    if (toPlay.Length == 0)
                    {
                        Debug.Log($"    • <color=#00ffff>{assignment.person}</color> -> <color=#ff7777>no playable interview</color>");
                        continue;
                    }

                    string sequence = string.Join(
                        " <color=#aaaaaa>+</color> ",
                        toPlay.Select(data => FormatInterviewForLog(data)).ToArray());

                    Debug.Log($"    • Person <b><color=#00ffff>{assignment.person}</color></b> -> {sequence}");

                    simulatedPlayedPersons.Add(assignment.person);
                    for (int j = 0; j < toPlay.Length; j++)
                    {
                        if (toPlay[j].isIntro)
                        {
                            continue;
                        }

                        if (!simulatedPlayedLevels.TryGetValue(toPlay[j].person, out HashSet<int> levels))
                        {
                            levels = new HashSet<int>();
                            simulatedPlayedLevels[toPlay[j].person] = levels;
                        }

                        levels.Add(toPlay[j].level);
                    }
                }
            }

            List<Tunnel> outs = GetAvailableOuts(currentSalle);
            if (outs.Count == 0)
            {
                Debug.Log($"  <color=#ff7777>No outgoing tunnel from</color> <b><color=#ffff55>{currentSalle.name}</color></b>");
                break;
            }

            List<Tunnel> preferredOuts = outs.Where(tunnel => tunnel.salleArrivee != null && !visitedSalles.Contains(tunnel.salleArrivee)).ToList();
            List<Tunnel> candidateOuts = preferredOuts.Count > 0 ? preferredOuts : outs;
            Tunnel chosenTunnel = candidateOuts[assignmentRandom.Next(candidateOuts.Count)];
            Salle nextSalle = chosenTunnel.salleArrivee;

            Debug.Log($"  <color=#55ff55>Go through</color> <b><color=#ffaa55>{chosenTunnel.name}</color></b> <color=#55ff55>to</color> <b><color=#ffff55>{nextSalle?.name ?? "None"}</color></b>");

            currentSalle = nextSalle;
            stepIndex++;
            safety++;
        }

        Debug.Log("<b><color=#55ff55>=== GAMEPLAY SIMULATION END ===</color></b>");
    }

    List<Tunnel> GetAvailableOuts(Salle salle)
    {
        if (salle == null)
        {
            return new List<Tunnel>();
        }

        return FindObjectsByType<Tunnel>(FindObjectsSortMode.None)
            .Where(tunnel => tunnel != null)
            .Where(tunnel => HasForwardPortal(tunnel))
            .Where(tunnel => tunnel.salleDepart == salle)
            .Where(tunnel => tunnel.salleArrivee != null)
            .ToList();
    }

    int GetSimulatedPlayCount(int roomInterviewCount)
    {
        if (roomInterviewCount <= 0)
        {
            return 0;
        }

        if (simulatedCuriosity <= 0f)
        {
            return 0;
        }

        if (simulatedCuriosity >= 1f)
        {
            return roomInterviewCount;
        }

        if (Mathf.Approximately(simulatedCuriosity, 0.5f))
        {
            return assignmentRandom.Next(1, roomInterviewCount + 1);
        }

        if (simulatedCuriosity < 0.5f)
        {
            float t = simulatedCuriosity / 0.5f;
            int maxCount = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(1f, roomInterviewCount, t)), 1, roomInterviewCount);
            return assignmentRandom.Next(1, maxCount + 1);
        }

        float highT = (simulatedCuriosity - 0.5f) / 0.5f;
        int minCount = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(1f, roomInterviewCount, highT)), 1, roomInterviewCount);
        return assignmentRandom.Next(minCount, roomInterviewCount + 1);
    }

    string FormatInterviewForLog(InterviewData interview)
    {
        string kind = interview.isIntro ? "INTRO" : "ITW";
        string color = interview.isIntro ? "#55ff55" : "#ffaa55";
        return $"<b><color={color}>[{kind}]</color></b> <color=#ffff55>{interview.filename}</color> <color=#aaaaaa>(P:</color><color=#00ffff>{interview.person}</color><color=#aaaaaa>, L:</color><color=#ff5500>{interview.level}</color><color=#aaaaaa>, N:</color><color=#cc55ee>{interview.note}</color><color=#aaaaaa>)</color>";
    }

    Dictionary<Salle, int> BuildForwardDepthMap(Salle[] salles, Tunnel[] tunnels)
    {
        Dictionary<Salle, int> depthMap = new Dictionary<Salle, int>();
        if (salles == null || salles.Length == 0)
        {
            return depthMap;
        }

        MainController controller = MainController.instance;
        Salle startSalle = controller != null ? controller.initialSalle : salles.FirstOrDefault();
        if (startSalle == null)
        {
            return depthMap;
        }

        Queue<Salle> queue = new Queue<Salle>();
        depthMap[startSalle] = 0;
        queue.Enqueue(startSalle);

        while (queue.Count > 0)
        {
            Salle current = queue.Dequeue();
            int currentDepth = depthMap[current];

            for (int i = 0; i < tunnels.Length; i++)
            {
                Tunnel tunnel = tunnels[i];
                if (tunnel == null || !HasForwardPortal(tunnel))
                {
                    continue;
                }

                if (tunnel.salleDepart != current || tunnel.salleArrivee == null)
                {
                    continue;
                }

                if (depthMap.ContainsKey(tunnel.salleArrivee))
                {
                    continue;
                }

                depthMap[tunnel.salleArrivee] = currentDepth + 1;
                queue.Enqueue(tunnel.salleArrivee);
            }
        }

        for (int i = 0; i < salles.Length; i++)
        {
            if (!depthMap.ContainsKey(salles[i]))
            {
                depthMap[salles[i]] = int.MaxValue;
            }
        }

        return depthMap;
    }

    Dictionary<Salle, int> BuildConnectivityMap(Salle[] salles, Tunnel[] tunnels)
    {
        Dictionary<Salle, int> connectivity = new Dictionary<Salle, int>();

        for (int i = 0; i < salles.Length; i++)
        {
            connectivity[salles[i]] = 0;
        }

        for (int i = 0; i < tunnels.Length; i++)
        {
            Tunnel tunnel = tunnels[i];
            if (tunnel == null || tunnel.salleDepart == null || tunnel.salleArrivee == null)
            {
                continue;
            }

            if (!HasForwardPortal(tunnel))
            {
                continue;
            }

            if (connectivity.ContainsKey(tunnel.salleDepart))
            {
                connectivity[tunnel.salleDepart]++;
            }

            if (connectivity.ContainsKey(tunnel.salleArrivee))
            {
                connectivity[tunnel.salleArrivee]++;
            }
        }

        return connectivity;
    }

    bool HasForwardPortal(Tunnel tunnel)
    {
        Transform portal = tunnel.transform.Find("Portal");
        return portal != null && portal.GetComponentInChildren<KataPortal>() != null;
    }

    PersonInterviewStats? GetPersonStats(string person)
    {
        for (int i = 0; i < personStatsList.Count; i++)
        {
            if (string.Equals(personStatsList[i].person, person, StringComparison.OrdinalIgnoreCase))
            {
                return personStatsList[i];
            }
        }

        return null;
    }

    bool HasVisitedAnyInterview(PersonInterviewStats stats)
    {
        if (!string.IsNullOrEmpty(stats.introInterview.filename) && stats.introInterview.visited)
        {
            return true;
        }

        for (int i = 0; i < stats.interviews.Count; i++)
        {
            if (stats.interviews[i].visited)
            {
                return true;
            }
        }

        return false;
    }

    InterviewData? GetBestAvailableInterview(List<InterviewData> candidates)
    {
        List<InterviewData> available = new List<InterviewData>();

        for (int i = 0; i < candidates.Count; i++)
        {
            InterviewData interview = candidates[i];
            if (interview.visited)
            {
                continue;
            }

            if (IsInterviewAllowedForPlayback(interview))
            {
                available.Add(interview);
            }
        }

        if (available.Count == 0)
        {
            return null;
        }

        List<InterviewData> leveledCandidates = ApplyLevelBias(available);
        int bestNote = leveledCandidates.Max(data => data.note);
        List<InterviewData> bestNoteCandidates = leveledCandidates.Where(data => data.note == bestNote).ToList();
        int randomIndex = assignmentRandom != null ? assignmentRandom.Next(bestNoteCandidates.Count) : UnityEngine.Random.Range(0, bestNoteCandidates.Count);
        return bestNoteCandidates[randomIndex];
    }

    static string[] SplitCsvLine(string line)
    {
        return line.Split(',');
    }

    static bool IsEmptyRow(string[] fields)
    {
        if (fields == null || fields.Length == 0)
        {
            return true;
        }

        for (int i = 0; i < fields.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(fields[i]))
            {
                return false;
            }
        }

        return true;
    }

    static string GetField(string[] fields, int index)
    {
        if (fields == null || index < 0 || index >= fields.Length)
        {
            return string.Empty;
        }

        return fields[index].Trim();
    }

    static int ParseInt(string value, int defaultValue = 0)
    {
        if (int.TryParse(value, out int result))
        {
            return result;
        }

        return defaultValue;
    }

    void logAssignments()
    {
        Debug.Log("Current Room Person Assignments:");
        for (int i = 0; i < roomAssignments.Count; i++)
        {
            var assignment = roomAssignments[i];
            string salleName = assignment.salle != null ? assignment.salle.name : "None";
            string slotName = assignment.interviewSlot != null ? assignment.interviewSlot.name : "None";
            Debug.Log($"- {salleName} / {slotName}: {assignment.person}");
        }
    }
}
