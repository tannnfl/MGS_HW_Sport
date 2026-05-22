using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Run once via Tools > MGS Sport - Setup Scene.
/// Creates all missing GameObjects, UI elements, spawn points,
/// wires them to GameManager, and updates the Player prefab.
/// </summary>
public static class SceneSetupTool
{
    [MenuItem("Tools/MGS Sport - Setup Scene")]
    public static void SetupScene()
    {
        // ── 1. Locate required existing objects ──────────────────────────────
        var gameManagerGO = GameObject.Find("GameManager");
        var canvasGO      = GameObject.Find("Canvas");

        if (gameManagerGO == null) { Debug.LogError("[SetupTool] GameManager not found in scene."); return; }
        if (canvasGO == null)      { Debug.LogError("[SetupTool] Canvas not found in scene."); return; }

        // ── 2. Thrower spawn points (2 total) ────────────────────────────────
        var throwerSpawn0 = GetOrCreate("ThrowerSpawnPos",  new Vector3(-7f,  1f, 0f));
        var throwerSpawn1 = GetOrCreate("ThrowerSpawnPos1", new Vector3(-7f, -1f, 0f));

        // Move the existing spawn to the right place (it was at -6.8, -0.14)
        throwerSpawn0.transform.position = new Vector3(-7f, 1f, 0f);

        // ── 3. Dodger spawn points (8 total) ─────────────────────────────────
        Vector3[] dodgerPos =
        {
            new Vector3(3f, 2.5f, 0f),
            new Vector3(3f, 0.5f, 0f),
            new Vector3(3f,-1.5f, 0f),
            new Vector3(5f, 2.5f, 0f),
            new Vector3(5f, 0.5f, 0f),
            new Vector3(5f,-1.5f, 0f),
            new Vector3(7f, 1.5f, 0f),
            new Vector3(7f,-0.5f, 0f),
        };

        // Reuse the existing DodgerSpawnPos for index 0
        var existingDodger = GameObject.Find("DodgerSpawnPos");
        if (existingDodger != null) existingDodger.transform.position = dodgerPos[0];

        var dodgerSpawns = new GameObject[8];
        dodgerSpawns[0] = existingDodger != null
            ? existingDodger
            : CreateEmpty("DodgerSpawnPos", dodgerPos[0]);

        for (int i = 1; i < 8; i++)
            dodgerSpawns[i] = GetOrCreate("DodgerSpawnPos" + i, dodgerPos[i]);

        // ── 4. Ball spawn point — OUTSIDE DodgerRegion (radius ≈4.63) ────────
        // Placing at x=-5 keeps it in the thrower ring so it never spawns inside
        // the dodger zone, which would get it immediately stuck.
        var ballSpawn = GetOrCreate("BallSpawnPos", new Vector3(-5f, 0f, 0f));
        ballSpawn.transform.position = new Vector3(-5f, 0f, 0f);

        // ── 4b. Enable DodgerRegion (was inactive in the scene) ──────────────
        var dodgerRegionGO = GameObject.Find("DodgerRegion");
        if (dodgerRegionGO != null)
            dodgerRegionGO.SetActive(true);

        // ── 5. UI elements ───────────────────────────────────────────────────
        var startBtn      = GetOrCreateButton   (canvasGO, "StartButton",     "Start Game",     new Vector2(  0f, -150f), new Vector2(200f,  60f));
        var timerText     = GetOrCreateTMPText  (canvasGO, "TimerText",       "Time: --",       new Vector2(  0f,  250f), new Vector2(300f,  60f), 36f);
        var leaderboard   = GetOrCreateTMPText  (canvasGO, "LeaderboardText", "=== Scores ===", new Vector2(-420f,   0f), new Vector2(300f, 500f), 18f);
        var roundInfo     = GetOrCreateTMPText  (canvasGO, "RoundInfoText",   "",               new Vector2(  0f,  150f), new Vector2(700f,  80f), 30f);
        var nameInput     = GetOrCreateInputField(canvasGO, "NameInputField", "Enter your name...", new Vector2(0f, -230f), new Vector2(320f, 50f));

        // ── 6. Wire GameManager fields via SerializedObject ──────────────────
        // GameManager has multiple MonoBehaviours; find the right one by its fields
        MonoBehaviour gmScript = null;
        foreach (var mb in gameManagerGO.GetComponents<MonoBehaviour>())
        {
            if (mb == null) continue;
            var so = new SerializedObject(mb);
            if (so.FindProperty("throwerSpawnPoints") != null)
            {
                gmScript = mb;
                break;
            }
        }

        if (gmScript == null)
        {
            Debug.LogError("[SetupTool] Could not find GameManager script component. Make sure it compiles cleanly first.");
            return;
        }

        var gso = new SerializedObject(gmScript);

        // Thrower spawn points array
        var throwerArr = gso.FindProperty("throwerSpawnPoints");
        throwerArr.ClearArray();
        throwerArr.arraySize = 2;
        throwerArr.GetArrayElementAtIndex(0).objectReferenceValue = throwerSpawn0.transform;
        throwerArr.GetArrayElementAtIndex(1).objectReferenceValue = throwerSpawn1.transform;

        // Dodger spawn points array
        var dodgerArr = gso.FindProperty("dodgerSpawnPoints");
        dodgerArr.ClearArray();
        dodgerArr.arraySize = 8;
        for (int i = 0; i < 8; i++)
            dodgerArr.GetArrayElementAtIndex(i).objectReferenceValue = dodgerSpawns[i].transform;

        gso.FindProperty("ballSpawnPoint").objectReferenceValue  = ballSpawn.transform;
        gso.FindProperty("startButton").objectReferenceValue     = startBtn;
        gso.FindProperty("timerText").objectReferenceValue       = timerText;
        gso.FindProperty("leaderboardText").objectReferenceValue = leaderboard;
        gso.FindProperty("roundInfoText").objectReferenceValue   = roundInfo;
        gso.FindProperty("nameInputField").objectReferenceValue  = nameInput;

        gso.ApplyModifiedProperties();
        Debug.Log("[SetupTool] GameManager wired.");

        // ── 7. Player prefab: add world-space InfoText child ─────────────────
        const string prefabPath = "Assets/Prefab/Player.prefab";
        var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefabAsset == null)
        {
            Debug.LogWarning("[SetupTool] Player prefab not found at " + prefabPath);
        }
        else
        {
            var prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);

            // Create InfoText child if missing
            var infoTransform = prefabRoot.transform.Find("InfoText");
            if (infoTransform == null)
            {
                var infoGO = new GameObject("InfoText");
                infoGO.transform.SetParent(prefabRoot.transform, false);
                infoGO.transform.localPosition = new Vector3(0f, 0.85f, 0f);
                infoGO.transform.localScale    = Vector3.one * 0.01f;

                var tmp = infoGO.AddComponent<TextMeshPro>();
                tmp.text      = "Role";
                tmp.fontSize  = 300f; // large because scale is 0.01
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color     = Color.white;

                infoTransform = infoGO.transform;
                Debug.Log("[SetupTool] Added InfoText child to Player prefab.");
            }

            // Wire localInfoText on PlayerComponent
            foreach (var mb in prefabRoot.GetComponents<MonoBehaviour>())
            {
                if (mb == null) continue;
                var pso = new SerializedObject(mb);
                var infoProp = pso.FindProperty("localInfoText");
                if (infoProp == null) continue;

                infoProp.objectReferenceValue = infoTransform.GetComponent<TextMeshPro>();
                pso.ApplyModifiedProperties();
                Debug.Log("[SetupTool] Wired localInfoText on PlayerComponent.");
                break;
            }
            // Note: throwerZone and dodgerZone are found at runtime via GameObject.Find —
            // prefabs cannot hold references to scene objects.

            PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }

        // ── 8. Mark scene dirty so Ctrl+S saves everything ───────────────────
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Debug.Log("[SetupTool] ✓ Done! Press Ctrl+S to save the scene.");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static GameObject GetOrCreate(string name, Vector3 pos)
    {
        var go = GameObject.Find(name);
        if (go == null) go = CreateEmpty(name, pos);
        return go;
    }

    private static GameObject CreateEmpty(string name, Vector3 pos)
    {
        var go = new GameObject(name);
        go.transform.position = pos;
        return go;
    }

    private static Button GetOrCreateButton(GameObject canvas, string name, string label, Vector2 anchoredPos, Vector2 size)
    {
        var existing = canvas.transform.Find(name);
        if (existing != null) return existing.GetComponent<Button>();

        var go = new GameObject(name);
        go.transform.SetParent(canvas.transform, false);
        go.layer = 5;

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin       = new Vector2(0.5f, 0.5f);
        rt.anchorMax       = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta       = size;

        var img = go.AddComponent<Image>();
        img.color = new Color(0.2f, 0.55f, 1f);

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;

        // Label
        var labelGO = new GameObject("Label");
        labelGO.transform.SetParent(go.transform, false);
        labelGO.layer = 5;

        var labelRT = labelGO.AddComponent<RectTransform>();
        labelRT.anchorMin  = Vector2.zero;
        labelRT.anchorMax  = Vector2.one;
        labelRT.offsetMin  = Vector2.zero;
        labelRT.offsetMax  = Vector2.zero;

        var tmp = labelGO.AddComponent<TextMeshProUGUI>();
        tmp.text      = label;
        tmp.fontSize  = 24f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color     = Color.white; // white on blue button — stays white

        return btn;
    }

    private static TextMeshProUGUI GetOrCreateTMPText(GameObject canvas, string name, string defaultText, Vector2 anchoredPos, Vector2 size, float fontSize)
    {
        var existing = canvas.transform.Find(name);
        if (existing != null) return existing.GetComponent<TextMeshProUGUI>();

        var go = new GameObject(name);
        go.transform.SetParent(canvas.transform, false);
        go.layer = 5;

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin       = new Vector2(0.5f, 0.5f);
        rt.anchorMax       = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta       = size;

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = defaultText;
        tmp.fontSize  = fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color     = new Color(0.1f, 0.1f, 0.1f); // near-black for white bg

        return tmp;
    }

    private static TMP_InputField GetOrCreateInputField(GameObject canvas, string name, string placeholder, Vector2 anchoredPos, Vector2 size)
    {
        var existing = canvas.transform.Find(name);
        if (existing != null) return existing.GetComponent<TMP_InputField>();

        // Root
        var go = new GameObject(name);
        go.transform.SetParent(canvas.transform, false);
        go.layer = 5;

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin       = new Vector2(0.5f, 0.5f);
        rt.anchorMax       = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta       = size;

        var img = go.AddComponent<Image>();
        img.color = new Color(0.88f, 0.88f, 0.88f, 0.9f); // light gray input bg

        var input = go.AddComponent<TMP_InputField>();

        // Text Area
        var areaGO = new GameObject("Text Area");
        areaGO.transform.SetParent(go.transform, false);
        areaGO.layer = 5;

        var areaRT = areaGO.AddComponent<RectTransform>();
        areaRT.anchorMin = Vector2.zero;
        areaRT.anchorMax = Vector2.one;
        areaRT.offsetMin = new Vector2(10f,  6f);
        areaRT.offsetMax = new Vector2(-10f, -6f);
        areaGO.AddComponent<RectMask2D>();

        // Placeholder
        var phGO = new GameObject("Placeholder");
        phGO.transform.SetParent(areaGO.transform, false);
        phGO.layer = 5;

        var phRT = phGO.AddComponent<RectTransform>();
        phRT.anchorMin = Vector2.zero; phRT.anchorMax = Vector2.one;
        phRT.offsetMin = Vector2.zero; phRT.offsetMax = Vector2.zero;

        var phTMP = phGO.AddComponent<TextMeshProUGUI>();
        phTMP.text      = placeholder;
        phTMP.fontSize  = 20f;
        phTMP.color     = new Color(0.45f, 0.45f, 0.45f); // medium gray placeholder
        phTMP.alignment = TextAlignmentOptions.MidlineLeft;

        // Input text
        var textGO = new GameObject("Text");
        textGO.transform.SetParent(areaGO.transform, false);
        textGO.layer = 5;

        var textRT = textGO.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero; textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero; textRT.offsetMax = Vector2.zero;

        var textTMP = textGO.AddComponent<TextMeshProUGUI>();
        textTMP.text      = "";
        textTMP.fontSize  = 20f;
        textTMP.color     = new Color(0.1f, 0.1f, 0.1f); // near-black input text
        textTMP.alignment = TextAlignmentOptions.MidlineLeft;

        input.textComponent = textTMP;
        input.placeholder   = phTMP;

        return input;
    }
}
