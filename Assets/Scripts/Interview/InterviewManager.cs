using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

[ExecuteAlways]
public class InterviewManager : MonoBehaviour
{
    [Serializable]
    public struct SalleAssignment
    {
        public Salle salle;
        public List<InterviewData> interviews;
    }

    public struct InterviewData
    {
        public string person;
        public string filename;
        public string theme;
        public bool isIntro;
        public bool visited;
        public string depthkitId;
        public int level;
        public int note;
    };

    public struct PersonInterviewStats
    {
        public string person;
        public InterviewData introInterview;
        public List<InterviewData> interviews;
    }

    List<InterviewData> interviewDataList = new List<InterviewData>();
    List<PersonInterviewStats> personStatsList = new List<PersonInterviewStats>();
    List<SalleAssignment> salleAssignments = new List<SalleAssignment>();
    System.Random assignmentRandom;

    void OnEnable()
    {
        LoadInterviewData();
        BuildSalleAssignments();
        ApplyAssignmentsToScene();

        logStatsByPerson();
        logAssignments();
    }

    void Update()
    {

    }


    // API to get interview data

    public void ResetGame()
    {
        for (int i = 0; i < interviewDataList.Count; i++)
        {
            var data = interviewDataList[i];
            data.visited = false;
            interviewDataList[i] = data;
        }

        RebuildPersonStats();
        BuildSalleAssignments();
        ApplyAssignmentsToScene();
    }

    public void ClearVisits()
    {
        ResetGame();
    }

    public bool MarkInterviewVisited(string interviewId)
    {
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
            interviewDataList[i] = data;
            RebuildPersonStats();
            BuildSalleAssignments();
            ApplyAssignmentsToScene();
            return true;
        }

        return false;
    }

    public InterviewData[] GetNextInterviewsForPerson(string person)
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
        bool hasVisitedAny = HasVisitedAnyInterview(stats.Value);

        if (!hasVisitedAny && !string.IsNullOrEmpty(stats.Value.introInterview.filename))
        {
            result.Add(stats.Value.introInterview);
        }

        InterviewData? nextInterview = GetBestAvailableInterview(stats.Value.interviews);
        if (nextInterview.HasValue)
        {
            result.Add(nextInterview.Value);
        }

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

    public void AssignInterviewsToSalles(int? seed = null)
    {
        if (seed.HasValue)
        {
            assignmentRandom = new System.Random(seed.Value);
        }
        else if (assignmentRandom == null)
        {
            assignmentRandom = new System.Random(Environment.TickCount);
        }

        BuildSalleAssignments();
        ApplyAssignmentsToScene();
    }

    public void ApplyAssignmentsToScene()
    {
        for (int i = 0; i < salleAssignments.Count; i++)
        {
            SalleAssignment assignment = salleAssignments[i];
            if (assignment.salle == null)
            {
                continue;
            }

            Interview[] interviewSlots = assignment.salle.interviews;
            int count = Mathf.Min(interviewSlots.Length, assignment.interviews.Count);
            for (int j = 0; j < count; j++)
            {
                Interview slot = interviewSlots[j];
                InterviewData data = assignment.interviews[j];
                if (slot != null)
                {
                    slot.set(data.depthkitId, data.level);
                }
            }
        }
    }

    public InterviewData[] GetAssignedInterviewsForSalle(Salle salle)
    {
        if (salle == null)
        {
            return Array.Empty<InterviewData>();
        }

        for (int i = 0; i < salleAssignments.Count; i++)
        {
            if (salleAssignments[i].salle == salle)
            {
                return salleAssignments[i].interviews.ToArray();
            }
        }

        return Array.Empty<InterviewData>();
    }

    public InterviewData[] GetNextInterviewsForTheme(string theme)
    {
        if (string.IsNullOrWhiteSpace(theme))
        {
            return Array.Empty<InterviewData>();
        }

        List<InterviewData> candidates = interviewDataList.FindAll(data =>
            !data.isIntro &&
            string.Equals(data.theme, theme, StringComparison.OrdinalIgnoreCase));

        InterviewData? nextInterview = GetBestAvailableInterview(candidates);
        if (!nextInterview.HasValue)
        {
            return Array.Empty<InterviewData>();
        }

        PersonInterviewStats? stats = GetPersonStats(nextInterview.Value.person);
        if (!stats.HasValue)
        {
            return new[] { nextInterview.Value };
        }

        List<InterviewData> result = new List<InterviewData>();
        bool hasVisitedAny = HasVisitedAnyInterview(stats.Value);

        if (!hasVisitedAny && !string.IsNullOrEmpty(stats.Value.introInterview.filename))
        {
            result.Add(stats.Value.introInterview);
        }

        result.Add(nextInterview.Value);
        return result.ToArray();
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
                level = ParseInt(GetField(fields, 2)),
                note = ParseInt(GetField(fields, 3)),
                person = GetField(fields, 4),
                theme = GetField(fields, 5),
                visited = false
            };

            data.isIntro = string.Equals(data.theme, "Intro", StringComparison.OrdinalIgnoreCase);
            if (data.isIntro && data.level <= 0)
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

    void BuildSalleAssignments()
    {
        salleAssignments.Clear();

        if (assignmentRandom == null)
        {
            assignmentRandom = new System.Random(Environment.TickCount);
        }

        Salle[] salles = FindObjectsByType<Salle>(FindObjectsSortMode.None)
            .Where(salle => salle != null && !salle.isExit)
            .ToArray();

        Tunnel[] tunnels = FindObjectsByType<Tunnel>(FindObjectsSortMode.None);
        Dictionary<Salle, int> connectivity = BuildConnectivityMap(salles, tunnels);
        Dictionary<Salle, int> depthMap = BuildForwardDepthMap(salles, tunnels);

        List<Salle> shuffledSalles = salles
            .OrderBy(salle => depthMap.ContainsKey(salle) ? depthMap[salle] : int.MaxValue)
            .ThenByDescending(salle => connectivity.ContainsKey(salle) ? connectivity[salle] : 0)
            .ThenBy(_ => assignmentRandom.Next())
            .ToList();

        if (shuffledSalles.Count == 0)
        {
            return;
        }

        for (int i = 0; i < shuffledSalles.Count; i++)
        {
            salleAssignments.Add(new SalleAssignment
            {
                salle = shuffledSalles[i],
                interviews = new List<InterviewData>()
            });
        }

        List<InterviewData> availablePool = GetAssignableInterviewPool();
        HashSet<string> introducedPeople = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < personStatsList.Count; i++)
        {
            if (HasIntroBeenPlayed(personStatsList[i].person))
            {
                introducedPeople.Add(personStatsList[i].person);
            }
        }

        for (int i = 0; i < salleAssignments.Count; i++)
        {
            SalleAssignment assignment = salleAssignments[i];
            int slotCount = assignment.salle != null ? assignment.salle.interviews.Length : 0;

            for (int slotIndex = 0; slotIndex < slotCount; slotIndex++)
            {
                int salleDepth = depthMap.ContainsKey(assignment.salle) ? depthMap[assignment.salle] : int.MaxValue;
                InterviewData? next = GetBestInterviewForSalle(assignment.salle, availablePool, salleDepth, introducedPeople);
                if (!next.HasValue)
                {
                    break;
                }

                assignment.interviews.Add(next.Value);
                if (next.Value.isIntro)
                {
                    introducedPeople.Add(next.Value.person);
                }
                RemoveInterviewFromPool(availablePool, next.Value);
            }

            salleAssignments[i] = assignment;
        }

        for (int i = 0; i < salleAssignments.Count; i++)
        {
            var assignment = salleAssignments[i];
            assignment.interviews = assignment.interviews
                .OrderBy(data => data.isIntro ? 0 : 1)
                .ThenByDescending(data => data.note)
                .ThenBy(data => data.level)
                .ToList();
            salleAssignments[i] = assignment;
        }
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

    List<InterviewData> GetAssignableInterviewPool()
    {
        return interviewDataList
            .Where(data => !data.visited)
            .Where(data => !data.isIntro || !HasIntroBeenPlayed(data.person))
            .OrderBy(_ => assignmentRandom.Next())
            .ToList();
    }

    InterviewData? GetBestInterviewForSalle(Salle salle, List<InterviewData> pool, int salleDepth, HashSet<string> introducedPeople)
    {
        List<InterviewData> candidates = new List<InterviewData>();

        for (int i = 0; i < pool.Count; i++)
        {
            InterviewData candidate = pool[i];

            if (candidate.isIntro && HasIntroBeenPlayed(candidate.person))
            {
                continue;
            }

            if (!candidate.isIntro && !IsInterviewUnlocked(candidate))
            {
                continue;
            }

            if (!candidate.isIntro && !introducedPeople.Contains(candidate.person))
            {
                continue;
            }

            candidates.Add(candidate);
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        int effectiveDepth = salleDepth == int.MaxValue ? 99 : salleDepth;
        candidates = candidates
            .OrderBy(data => GetSalleAssignmentScore(data, effectiveDepth))
            .ThenBy(_ => assignmentRandom.Next())
            .ToList();

        return candidates[0];
    }

    int GetSalleAssignmentScore(InterviewData data, int salleDepth)
    {
        int score = 0;

        if (data.isIntro)
        {
            score += salleDepth <= 1 ? -100 : 40 + salleDepth * 10;
        }
        else
        {
            score += Mathf.Abs(data.level - Mathf.Max(1, salleDepth + 1)) * 20;
            score -= data.note * 3;
        }

        if (HasIntroBeenPlayed(data.person))
        {
            score -= 5;
        }

        return score;
    }

    void RemoveInterviewFromPool(List<InterviewData> pool, InterviewData interview)
    {
        for (int i = 0; i < pool.Count; i++)
        {
            if (string.Equals(pool[i].filename, interview.filename, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(pool[i].depthkitId, interview.depthkitId, StringComparison.OrdinalIgnoreCase))
            {
                pool.RemoveAt(i);
                return;
            }
        }
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

            if (IsInterviewUnlocked(interview))
            {
                available.Add(interview);
            }
        }

        if (available.Count == 0)
        {
            return null;
        }

        available.Sort(CompareInterviews);
        return available[0];
    }

    bool IsInterviewUnlocked(InterviewData candidate)
    {
        if (candidate.level <= 1)
        {
            return true;
        }

        for (int i = 0; i < interviewDataList.Count; i++)
        {
            InterviewData interview = interviewDataList[i];
            if (interview.isIntro)
            {
                continue;
            }

            if (!string.Equals(interview.person, candidate.person, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (interview.level == candidate.level - 1 && interview.visited)
            {
                return true;
            }
        }

        return false;
    }

    int CompareInterviews(InterviewData a, InterviewData b)
    {
        int noteCompare = b.note.CompareTo(a.note);
        if (noteCompare != 0)
        {
            return noteCompare;
        }

        int levelCompare = a.level.CompareTo(b.level);
        if (levelCompare != 0)
        {
            return levelCompare;
        }

        return string.Compare(a.filename, b.filename, StringComparison.OrdinalIgnoreCase);
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

    static int ParseInt(string value)
    {
        if (int.TryParse(value, out int result))
        {
            return result;
        }

        return 0;
    }

    void logStatsByPerson()
    {
        Debug.Log("Interview Stats by Person:");

        for (int i = 0; i < personStatsList.Count; i++)
        {
            var stats = personStatsList[i];
            bool hasIntro = !string.IsNullOrEmpty(stats.introInterview.filename);

            if (!hasIntro)
            {
                Debug.LogWarning($"No intro interview found for {stats.person}");
            }

            int visitedCount = hasIntro && stats.introInterview.visited ? 1 : 0;
            visitedCount += stats.interviews.Count(data => data.visited);

            int totalCount = stats.interviews.Count + (hasIntro ? 1 : 0);
            Debug.Log($"- {stats.person}: {visitedCount}/{totalCount} interviews visited");
        }
    }

    void logAssignments()
    {
        Debug.Log("Current Salle Assignments:");
        for (int i = 0; i < salleAssignments.Count; i++)
        {
            var assignment = salleAssignments[i];
            string salleName = assignment.salle != null ? assignment.salle.name : "None";
            Debug.Log($"- {salleName}: {assignment.interviews.Count} interviews assigned");
            for (int j = 0; j < assignment.interviews.Count; j++)
            {
                var interview = assignment.interviews[j];
                Debug.Log($"   - {interview.person} (Level {interview.level}, Note {interview.note}, Intro: {interview.isIntro})");
            }
        }
    }
}
