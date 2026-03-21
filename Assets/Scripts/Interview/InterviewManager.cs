using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using System.Globalization; // Required for CultureInfo

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

    public struct ResolvedInterviewPlayback
    {
        public InterviewData[] sequence;
        public InterviewData playedInterview;
    }

    public struct InterviewData
    {
        public string person;
        public string filename;
        public string mediaPath;
        public List<string> themes;
        public List<int> levels;
        public bool isIntro;
        public bool visited;
        public bool proposed;
        public string depthkitId;
        public string depthkitPath;
        public int level;
        public int note;
        public Vector3 offset;
        public float angle;
        public List<float> cutTimes;

        public override string ToString()
        {
            return $"InterviewData(person={person}, filename={filename}, themes=[{string.Join(", ", themes ?? new List<string>())}], levels=[{string.Join(", ", levels ?? new List<int>())}], isIntro={isIntro}, visited={visited}, proposed={proposed}, depthkitId={depthkitId}, depthkitPath={depthkitPath}, level={level}, note={note}, offset={offset}, angle={angle}, cuts=[{string.Join(", ", cutTimes ?? new List<float>())}])";
        }
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
    readonly HashSet<string> playedPersonsSinceAssignment = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    readonly List<string> playedThemesHistory = new List<string>();
    readonly Dictionary<Interview, RoomPersonAssignment> activeAssignmentsBySlot = new Dictionary<Interview, RoomPersonAssignment>();
    readonly Dictionary<Interview, InterviewData[]> resolvedPlaybackBySlot = new Dictionary<Interview, InterviewData[]>();
    readonly HashSet<Interview> consumedSlots = new HashSet<Interview>();
    Interview activePlayingSlot;
    Salle lastAssignedSalle;

    System.Random assignmentRandom;

    public bool generateAssignment;
    public bool simulateGameplay;
    [Range(0f, 1f)]
    public float simulatedCuriosity = 0.5f;

    MainController mainController;

    DataManager dataManager;

    void Start()
    {
        dataManager = GameObject.FindAnyObjectByType<DataManager>();
        mainController = GameObject.FindAnyObjectByType<MainController>();
        assignmentRandom = new System.Random(Environment.TickCount);
        LoadInterviewData();
    }

    void Update()
    {
        Salle currentSalle = mainController != null ? mainController.salle : null;
        if (currentSalle != lastAssignedSalle)
        {
            consumedSlots.Clear();
            roomAssignments.Clear();
            RefreshAssignmentsForCurrentSalle();
        }

        if (generateAssignment)
        {
            generateAssignment = false;
            RefreshAssignmentsForCurrentSalle();
        }

        if (!Application.isPlaying)
        {
            if (simulateGameplay)
            {
                simulateGameplay = false;
                SimulateGameplay();
            }
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

        playedPersonsSinceAssignment.Clear();
        playedThemesHistory.Clear();
        consumedSlots.Clear();
        RebuildPersonStats();
    }

    public void ClearVisits()
    {
        ResetGame();
    }

    public bool MarkInterviewVisited(string interviewId)
    {
        return MarkInterviewVisited(interviewId, null);
    }

    public bool MarkInterviewSequenceVisited(InterviewData[] sequence)
    {
        if (sequence == null || sequence.Length == 0)
        {
            return false;
        }

        InterviewData? playedInterview = null;

        for (int i = 0; i < sequence.Length; i++)
        {
            InterviewData current = sequence[i];
            if (!MarkInterviewVisitedInternal(current.filename, current.depthkitId, current.levels))
            {
                continue;
            }

            if (!current.isIntro)
            {
                playedInterview = GetInterviewDataByFilename(current.filename);
            }
        }

        if (!playedInterview.HasValue)
        {
            return false;
        }

        RegisterPlayedInterview(playedInterview.Value);
        ClearProposedFlags();
        RebuildPersonStats();
        return true;
    }

    public bool MarkInterviewVisitedByClip(string depthkitId, int level)
    {
        return MarkInterviewVisited(depthkitId, level);
    }

    bool MarkInterviewVisited(string interviewId, int? level)
    {
        InterviewData? matchedInterview = MarkInterviewVisitedInternal(interviewId, interviewId, level.HasValue ? new List<int> { level.Value } : null)
            ? GetInterviewData(interviewId, level)
            : null;

        if (!matchedInterview.HasValue)
        {
            return false;
        }

        RegisterPlayedInterview(matchedInterview.Value);
        ClearProposedFlags();
        RebuildPersonStats();
        return true;
    }

    public void MarkSlotConsumed(Interview slot)
    {
        if (slot == null)
        {
            return;
        }

        consumedSlots.Add(slot);
    }

    public bool IsSlotConsumed(Interview slot)
    {
        return slot != null && consumedSlots.Contains(slot);
    }

    public void NotifyInterviewStarted(Interview slot)
    {
        if (slot == null)
        {
            return;
        }

        if (activePlayingSlot != null && activePlayingSlot != slot)
        {
            activePlayingSlot.StopPlaybackForAnotherInterview();
        }

        activePlayingSlot = slot;
    }

    public void NotifyInterviewStopped(Interview slot)
    {
        if (activePlayingSlot == slot)
        {
            activePlayingSlot = null;
        }
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

        RefreshAssignmentsForCurrentSalle();
    }

    public void ApplyAssignmentsToScene()
    {
        ApplyAssignmentsToScene(roomAssignments);
    }

    public void RefreshAssignmentsForSalle(Salle salle)
    {
        consumedSlots.Clear();
        resolvedPlaybackBySlot.Clear();
        ClearProposedFlags();

        if (salle == null || salle.isExit)
        {
            roomAssignments.Clear();
            activeAssignmentsBySlot.Clear();
            lastAssignedSalle = salle;
            return;
        }

        if (roomAssignments.Count == 0 || roomAssignments.Any(assignment => assignment.salle != salle))
        {
            roomAssignments.Clear();
            roomAssignments.AddRange(BuildAssignmentsForSalle(salle, consumedSlots));
        }

        ApplyAssignmentsToScene(roomAssignments);
        lastAssignedSalle = salle;
        logAssignments();
    }

    void ApplyAssignmentsToScene(List<RoomPersonAssignment> assignments)
    {
        activeAssignmentsBySlot.Clear();
        resolvedPlaybackBySlot.Clear();

        if (assignments == null)
        {
            return;
        }

        for (int i = 0; i < assignments.Count; i++)
        {
            RoomPersonAssignment assignment = assignments[i];
            if (assignment.interviewSlot == null || consumedSlots.Contains(assignment.interviewSlot) || string.IsNullOrWhiteSpace(assignment.person))
            {
                continue;
            }

            InterviewData? selectedInterview = GetPreviewInterviewForAssignment(assignment);
            if (selectedInterview.HasValue && !string.IsNullOrWhiteSpace(selectedInterview.Value.depthkitPath))
            {
                assignment.interviewSlot.ResetForPreviewAssignment();
                if (selectedInterview.HasValue) assignment.interviewSlot.set(selectedInterview.Value);
                assignment.interviewSlot.load();
                activeAssignmentsBySlot[assignment.interviewSlot] = assignment;
            }
        }
    }

    public bool TryResolvePlaybackForSlot(Interview slot, out ResolvedInterviewPlayback playback)
    {
        playback = default;

        if (slot == null)
        {
            return false;
        }

        if (consumedSlots.Contains(slot))
        {
            return false;
        }

        if (resolvedPlaybackBySlot.TryGetValue(slot, out InterviewData[] cachedSequence) && cachedSequence != null && cachedSequence.Length > 0)
        {
            InterviewData? cachedPlayedInterview = GetPlayedInterviewFromSequence(cachedSequence);
            if (cachedPlayedInterview.HasValue)
            {
                playback = new ResolvedInterviewPlayback
                {
                    sequence = cachedSequence,
                    playedInterview = cachedPlayedInterview.Value
                };
                return true;
            }
        }

        if (!activeAssignmentsBySlot.TryGetValue(slot, out RoomPersonAssignment assignment) || string.IsNullOrWhiteSpace(assignment.person))
        {
            return false;
        }

        InterviewData[] sequence = GetInterviewsToPlayForAssignment(assignment);
        InterviewData? playedInterview = GetPlayedInterviewFromSequence(sequence);
        if (!playedInterview.HasValue)
        {
            return false;
        }

        MarkInterviewsAsProposed(sequence.ToList());
        resolvedPlaybackBySlot[slot] = sequence;
        playback = new ResolvedInterviewPlayback
        {
            sequence = sequence,
            playedInterview = playedInterview.Value
        };
        return true;
    }

    public InterviewData[] GetInterviewsToPlayForPerson(string person)
    {
        int? salleLevel = mainController != null && mainController.salle != null ? mainController.salle.niveau : null;
        return GetInterviewsToPlayForPerson(person, salleLevel, playedPersonsSinceAssignment, playedThemesHistory);
    }

    InterviewData[] GetInterviewsToPlayForAssignment(RoomPersonAssignment assignment)
    {
        int salleLevel = assignment.salle != null ? assignment.salle.niveau : 0;
        return GetInterviewsToPlayForPerson(assignment.person, salleLevel, playedPersonsSinceAssignment, playedThemesHistory);
    }

    InterviewData? GetPreviewInterviewForAssignment(RoomPersonAssignment assignment)
    {
        PersonInterviewStats? stats = GetPersonStats(assignment.person);
        if (!stats.HasValue)
        {
            return null;
        }

        int salleLevel = assignment.salle != null ? assignment.salle.niveau : 0;
        bool hasPlayedBefore = playedPersonsSinceAssignment.Contains(assignment.person) || HasVisitedAnyInterview(stats.Value);

        if (!hasPlayedBefore && !string.IsNullOrWhiteSpace(stats.Value.introInterview.depthkitId))
        {
            return stats.Value.introInterview;
        }

        InterviewData? bestInterview = GetBestInterviewForPerson(stats.Value.person, salleLevel, playedThemesHistory);
        if (bestInterview.HasValue)
        {
            return bestInterview.Value;
        }

        if (!string.IsNullOrWhiteSpace(stats.Value.introInterview.depthkitId))
        {
            return stats.Value.introInterview;
        }

        return null;
    }

    InterviewData[] GetInterviewsToPlayForPerson(string person, int? salleLevel, HashSet<string> playedPersons, List<string> themeHistory)
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
        bool hasPlayedBefore = playedPersons.Contains(person) || HasVisitedAnyInterview(stats.Value);

        if (!hasPlayedBefore && !string.IsNullOrEmpty(stats.Value.introInterview.filename))
        {
            result.Add(stats.Value.introInterview);
        }

        InterviewData? bestInterview = GetBestInterviewForPerson(stats.Value.person, salleLevel, themeHistory);
        if (bestInterview.HasValue)
        {
            result.Add(bestInterview.Value);
        }

        return result.ToArray();
    }

    public InterviewData? GetBestInterviewForPerson(string person)
    {
        return GetBestInterviewForPerson(person, null, playedThemesHistory);
    }

    InterviewData? GetBestInterviewForPerson(string person, int? salleLevel, List<string> themeHistory)
    {
        PersonInterviewStats? stats = GetPersonStats(person);
        if (!stats.HasValue)
        {
            return null;
        }

        List<InterviewData> candidates = GetPlayableCandidates(stats.Value.person, stats.Value.interviews, salleLevel);
        if (candidates.Count == 0)
        {
            return null;
        }

        List<InterviewData> themedCandidates = FilterCandidatesByThemeHistory(candidates, themeHistory);
        return PickBestByNote(themedCandidates);
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
            if (IsInterviewAllowedForPlayback(candidates[i], null))
            {
                playable.Add(candidates[i]);
            }
        }

        if (playable.Count == 0)
        {
            return Array.Empty<InterviewData>();
        }

        InterviewData? pickedInterview = PickBestByNote(FilterCandidatesByThemeHistory(playable, playedThemesHistory));
        if (!pickedInterview.HasValue)
        {
            return Array.Empty<InterviewData>();
        }

        InterviewData picked = pickedInterview.Value;

        List<InterviewData> result = new List<InterviewData>();
        PersonInterviewStats? pickedStats = GetPersonStats(picked.person);
        if (pickedStats.HasValue && !HasVisitedAnyInterview(pickedStats.Value))
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


        string csvPath = dataManager.GetRootFilePath("interviews/interviews.csv");
        if (!File.Exists(csvPath))
        {
            Debug.LogWarning("Interview CSV not found at " + csvPath);
            return;
        }

        Debug.Log("Loading interview data from " + csvPath);

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

            Debug.Log(line);
            List<string> offsets = SplitMultiValueField(GetField(fields, 6));
            Vector3 offset = new Vector3(getFloatValue(offsets[0]), 0, getFloatValue(offsets[1]));
            float angleOffset = getFloatValue(offsets[2]);



            InterviewData data = new InterviewData
            {
                filename = GetField(fields, 0),
                depthkitId = GetField(fields, 1),
                level = ParseInt(GetField(fields, 2), 1),
                note = ParseInt(GetField(fields, 3), 1),
                person = GetField(fields, 4),
                themes = SplitMultiValueField(GetField(fields, 5)),
                levels = ParseLevels(GetField(fields, 2)),
                offset = offset,
                angle = angleOffset,
                cutTimes = SplitMultiValueField(GetField(fields, 7)).Select(
                    s =>
                    {
                        Debug.Log("Checking time" + s);
                        //Format HH:MM:SS:FF where FF is frames at 30fps
                        string[] timeParts = s.Split(':');
                        if (timeParts.Length != 4)
                        {
                            Debug.LogWarning("Invalid cut time format: " + s);
                            return 0f;
                        }
                        int hours = getIntValue(timeParts[0]);
                        int minutes = getIntValue(timeParts[1]);
                        int seconds = getIntValue(timeParts[2]);
                        int frames = getIntValue(timeParts[3]);
                        float totalSeconds = hours * 3600 + minutes * 60 + seconds + frames / 30f;
                        return totalSeconds;
                    }
                    ).ToList(),
                visited = false,
                proposed = false
            };



            data.mediaPath = CombineInterviewFolder(data.person.Replace(" ", "_"), data.filename);
            data.depthkitPath = CombineInterviewFolder(data.person.Replace(" ", "_"), data.depthkitId);


            data.isIntro = data.themes.Any(t => string.Equals(t, "Intro", StringComparison.OrdinalIgnoreCase));
            if (data.levels.Count == 0)
            {
                data.levels.AddRange(new[] { 0, 1, 2, 3, 4 });
            }
            data.level = data.levels[0];
            interviewDataList.Add(data);
        }

        RebuildPersonStats();

        logAssignments();
    }


    float getFloatValue(string s)
    {
        if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
        {
            return result;
        }

        Debug.LogError("Failed to parse float. Check your input string format : " + s);
        return 0f;
    }

    int getIntValue(string s)
    {
        return int.Parse(s);
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
                    .OrderBy(data => data.levels.Count > 0 ? data.levels.Min() : data.level)
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

        Debug.Log("Rebuilt person stats for " + personStatsList.Count + " people.");
    }

    List<InterviewData> GetPlayableCandidates(string person, List<InterviewData> interviews)
    {
        return GetPlayableCandidates(person, interviews, null);
    }

    List<InterviewData> GetPlayableCandidates(string person, List<InterviewData> interviews, int? salleLevel)
    {
        return interviews
            .Where(data => !data.visited)
            .Where(data => !data.proposed)
            .Where(data => !salleLevel.HasValue || data.levels.Contains(salleLevel.Value))
            .ToList();
    }

    bool IsInterviewAllowedForPlayback(InterviewData candidate, int? salleLevel)
    {
        return !candidate.visited && !candidate.proposed && (!salleLevel.HasValue || candidate.levels.Contains(salleLevel.Value));
    }

    void RegisterPlayedInterview(InterviewData interview)
    {
        playedPersonsSinceAssignment.Add(interview.person);

        if (interview.isIntro)
        {
            return;
        }

        playedThemesHistory.Clear();

        if (interview.themes != null)
        {
            for (int i = 0; i < interview.themes.Count; i++)
            {
                string theme = interview.themes[i];
                if (!string.IsNullOrWhiteSpace(theme) && !string.Equals(theme, "Intro", StringComparison.OrdinalIgnoreCase))
                {
                    playedThemesHistory.Add(theme);
                }
            }
        }
    }

    void MarkInterviewsAsProposed(List<InterviewData> sequence)
    {
        if (sequence == null)
        {
            return;
        }

        for (int i = 0; i < sequence.Count; i++)
        {
            MarkInterviewAsProposed(sequence[i]);
        }
    }

    void MarkInterviewAsProposed(InterviewData interview)
    {
        for (int i = 0; i < interviewDataList.Count; i++)
        {
            if (!MatchesInterview(interviewDataList[i], interview.filename, interview.depthkitId, interview.levels))
            {
                continue;
            }

            var data = interviewDataList[i];
            data.proposed = true;
            interviewDataList[i] = data;
            return;
        }
    }

    void ClearProposedFlags()
    {
        for (int i = 0; i < interviewDataList.Count; i++)
        {
            var data = interviewDataList[i];
            data.proposed = false;
            interviewDataList[i] = data;
        }
    }

    bool MarkInterviewVisitedInternal(string interviewId, string depthkitId, List<int> levels)
    {
        for (int i = 0; i < interviewDataList.Count; i++)
        {
            if (!MatchesInterview(interviewDataList[i], interviewId, depthkitId, levels))
            {
                continue;
            }

            var data = interviewDataList[i];
            data.visited = true;
            data.proposed = false;
            interviewDataList[i] = data;
            return true;
        }

        return false;
    }

    InterviewData? GetInterviewData(string interviewId, int? level)
    {
        List<int> levels = level.HasValue ? new List<int> { level.Value } : null;
        for (int i = 0; i < interviewDataList.Count; i++)
        {
            if (MatchesInterview(interviewDataList[i], interviewId, interviewId, levels))
            {
                return interviewDataList[i];
            }
        }

        return null;
    }

    InterviewData? GetInterviewDataByFilename(string filename)
    {
        for (int i = 0; i < interviewDataList.Count; i++)
        {
            if (string.Equals(interviewDataList[i].filename, filename, StringComparison.OrdinalIgnoreCase))
            {
                return interviewDataList[i];
            }
        }

        return null;
    }

    InterviewData? GetPlayedInterviewFromSequence(InterviewData[] sequence)
    {
        if (sequence == null || sequence.Length == 0)
        {
            return null;
        }

        for (int i = sequence.Length - 1; i >= 0; i--)
        {
            if (!sequence[i].isIntro)
            {
                return sequence[i];
            }
        }

        return sequence[sequence.Length - 1];
    }

    bool MatchesInterview(InterviewData data, string filename, string depthkitId, List<int> levels)
    {
        bool matchesId =
            string.Equals(data.filename, filename, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(data.depthkitId, depthkitId, StringComparison.OrdinalIgnoreCase);

        if (!matchesId)
        {
            return false;
        }

        if (levels == null || levels.Count == 0)
        {
            return true;
        }

        return levels.Any(level => data.levels.Contains(level));
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
        List<string> simulatedThemeHistory = new List<string>();
        HashSet<Salle> visitedSalles = new HashSet<Salle>();
        HashSet<Interview> simulatedConsumedSlots = new HashSet<Interview>();

        Salle currentSalle = startSalle;
        Salle previousSalle = null;
        int stepIndex = 1;
        int safety = 0;
        int maxSteps = Mathf.Max(10, roomAssignments.Count * 3);

        Debug.Log("<b><color=#55ff55>=== GAMEPLAY SIMULATION START ===</color></b>");
        Debug.Log($"<color=#55ffff>Start salle:</color> <b><color=#ffff55>{currentSalle.name}</color></b>");

        while (currentSalle != null && safety < maxSteps)
        {
            if (currentSalle != previousSalle)
            {
                simulatedConsumedSlots.Clear();
            }

            List<RoomPersonAssignment> simulatedAssignments = BuildAssignmentsForSalle(currentSalle, simulatedConsumedSlots);
            visitedSalles.Add(currentSalle);
            List<RoomPersonAssignment> roomPeople = simulatedAssignments
                .Where(assignment => assignment.salle == currentSalle && !string.IsNullOrWhiteSpace(assignment.person))
                .ToList();

            Debug.Log($"<b><color=#ffaa55>STEP {stepIndex}</color></b> � Room <b><color=#ffff55>{currentSalle.name}</color></b>");

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
                    if (assignment.interviewSlot != null)
                    {
                        simulatedConsumedSlots.Add(assignment.interviewSlot);
                    }

                    int salleLevel = assignment.salle != null ? assignment.salle.niveau : 0;
                    InterviewData[] toPlay = GetInterviewsToPlayForPerson(assignment.person, salleLevel, simulatedPlayedPersons, simulatedThemeHistory);

                    if (toPlay.Length == 0)
                    {
                        Debug.Log($"    � <color=#00ffff>{assignment.person}</color> -> <color=#ff7777>no playable interview</color>");
                        continue;
                    }

                    string sequence = string.Join(
                        " <color=#aaaaaa>+</color> ",
                        toPlay.Select(data => FormatInterviewForLog(data)).ToArray());

                    Debug.Log($"    � Person <b><color=#00ffff>{assignment.person}</color></b> -> {sequence}");

                    simulatedPlayedPersons.Add(assignment.person);
                    InterviewData lastPlayedInterview = toPlay[toPlay.Length - 1];
                    if (!lastPlayedInterview.isIntro)
                    {
                        simulatedThemeHistory.Clear();
                    }
                    for (int j = 0; j < toPlay.Length; j++)
                    {
                        if (toPlay[j].isIntro)
                        {
                            continue;
                        }

                        if (toPlay[j].themes == null)
                        {
                            continue;
                        }

                        for (int k = 0; k < toPlay[j].themes.Count; k++)
                        {
                            string theme = toPlay[j].themes[k];
                            if (!string.IsNullOrWhiteSpace(theme) && !string.Equals(theme, "Intro", StringComparison.OrdinalIgnoreCase))
                            {
                                simulatedThemeHistory.Add(theme);
                            }
                        }
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

            previousSalle = currentSalle;
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

        if (stats.interviews == null)
        {
            return false;
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

    static string[] SplitCsvLine(string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return Array.Empty<string>();
        }

        List<string> fields = new List<string>();
        System.Text.StringBuilder current = new System.Text.StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (c == ',' && !inQuotes)
            {
                fields.Add(current.ToString());
                current.Length = 0;
                continue;
            }

            current.Append(c);
        }

        fields.Add(current.ToString());
        return fields.ToArray();
    }

    static List<string> SplitMultiValueField(string value)
    {
        return value
            .Split(',')
            .Select(t => t.Trim())
            .Where(t => !string.IsNullOrEmpty(t))
            .ToList();
    }

    static List<int> ParseLevels(string value)
    {
        List<int> levels = new List<int>();
        if (string.IsNullOrWhiteSpace(value))
        {
            return levels;
        }

        string[] parts = value.Split(new[] { ',', ';', '|', '/' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i].Trim();
            string[] rangeParts = part.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries);
            if (rangeParts.Length == 2 && int.TryParse(rangeParts[0].Trim(), out int startLevel) && int.TryParse(rangeParts[1].Trim(), out int endLevel))
            {
                int minLevel = Mathf.Min(startLevel, endLevel);
                int maxLevel = Mathf.Max(startLevel, endLevel);
                for (int level = minLevel; level <= maxLevel; level++)
                {
                    levels.Add(level);
                }
            }
            else if (int.TryParse(part, out int parsedLevel))
            {
                levels.Add(parsedLevel);
            }
        }

        return levels
            .Where(level => level >= 0 && level <= 4)
            .Distinct()
            .OrderBy(level => level)
            .ToList();
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

    static string CombineInterviewFolder(string person, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return string.Empty;
        }

        string normalizedRelativePath = relativePath.Replace("\\", "/").TrimStart('/');
        if (string.IsNullOrWhiteSpace(person))
        {
            return normalizedRelativePath;
        }

        string normalizedPerson = person.Replace("\\", "/").Trim('/');
        if (normalizedRelativePath.StartsWith(normalizedPerson + "/", StringComparison.OrdinalIgnoreCase))
        {
            return normalizedRelativePath;
        }

        return normalizedPerson + "/" + normalizedRelativePath;
    }

    void logAssignments()
    {
        Debug.Log("Current Room Person Assignments (Salle " + (mainController.salle != null ? mainController.salle.name : "None") + ")");
        for (int i = 0; i < roomAssignments.Count; i++)
        {
            var assignment = roomAssignments[i];
            string salleName = assignment.salle != null ? assignment.salle.name : "None";
            string slotName = assignment.interviewSlot != null ? assignment.interviewSlot.name : "None";
            Debug.Log($"- {salleName} / {slotName}: {assignment.person}");
        }
    }

    void RefreshAssignmentsForCurrentSalle()
    {
        if (assignmentRandom == null)
        {
            assignmentRandom = new System.Random(Environment.TickCount);
        }


        Salle currentSalle = mainController.salle;
        RefreshAssignmentsForSalle(currentSalle);
    }

    void BuildAssignmentsForSalle(Salle currentSalle)
    {
        roomAssignments.Clear();
        roomAssignments.AddRange(BuildAssignmentsForSalle(currentSalle, consumedSlots));
    }

    List<RoomPersonAssignment> BuildAssignmentsForSalle(Salle currentSalle, HashSet<Interview> consumedInterviewSlots)
    {
        List<RoomPersonAssignment> assignments = new List<RoomPersonAssignment>();
        Interview[] slots = currentSalle != null ? currentSalle.interviews : null;
        if (slots == null || slots.Length == 0)
        {
            return assignments;
        }
        Debug.Log("Build assignments for salle " + currentSalle.name + " with " + slots.Length + " slots, stats list : " + (personStatsList.Count > 0 ? personStatsList[0].person : "None"));

        int salleLevel = currentSalle.niveau;
        List<string> candidatePersons = personStatsList
            .Where(stats => stats.interviews.Any(interview => !interview.visited && interview.levels.Contains(salleLevel)))
            .Select(stats => stats.person)
            .OrderBy(_ => assignmentRandom.Next())
            .ToList();

        if (candidatePersons.Count == 0)
        {
            candidatePersons = personStatsList
                .Select(stats => stats.person)
                .OrderBy(_ => assignmentRandom.Next())
                .ToList();
        }

        List<Interview> availableSlots = slots
            .Where(slot => slot != null)
            .Where(slot => consumedInterviewSlots == null || !consumedInterviewSlots.Contains(slot))
            .ToList();

        while (candidatePersons.Count > 0 && candidatePersons.Count < availableSlots.Count)
        {
            candidatePersons.Add(candidatePersons[assignmentRandom.Next(candidatePersons.Count)]);
        }

        for (int i = 0; i < availableSlots.Count && i < candidatePersons.Count; i++)
        {
            assignments.Add(new RoomPersonAssignment
            {
                salle = currentSalle,
                interviewSlot = availableSlots[i],
                person = candidatePersons[i]
            });
        }

        return assignments;
    }

    List<InterviewData> FilterCandidatesByThemeHistory(List<InterviewData> candidates, List<string> themeHistory)
    {
        if (candidates == null || candidates.Count == 0)
        {
            return new List<InterviewData>();
        }

        if (themeHistory == null || themeHistory.Count == 0)
        {
            return candidates;
        }

        HashSet<string> historySet = new HashSet<string>(themeHistory, StringComparer.OrdinalIgnoreCase);
        List<InterviewData> themedCandidates = candidates
            .Where(candidate => candidate.themes != null && candidate.themes.Any(theme => historySet.Contains(theme)))
            .ToList();

        return themedCandidates.Count > 0 ? themedCandidates : candidates;
    }

    InterviewData? PickBestByNote(List<InterviewData> candidates)
    {
        if (candidates == null || candidates.Count == 0)
        {
            return null;
        }

        int bestNote = candidates.Max(data => data.note);
        List<InterviewData> bestNoteCandidates = candidates
            .Where(data => data.note == bestNote)
            .ToList();

        if (bestNoteCandidates.Count == 1)
        {
            return bestNoteCandidates[0];
        }

        int randomIndex = assignmentRandom != null ? assignmentRandom.Next(bestNoteCandidates.Count) : UnityEngine.Random.Range(0, bestNoteCandidates.Count);
        return bestNoteCandidates[randomIndex];
    }
}
