using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using TMPro;

public class ARGitVisualizer : MonoBehaviour
{
    [Header("3D Git Prefabs (Auto-Generated if Empty)")]
    public GameObject commitNodePrefab;
    public GameObject headPointerPrefab;
    public GameObject conflictMarkerPrefab;

    [Header("Branch Materials (Auto-Generated if Empty)")]
    public Material mainBranchMat;
    public Material featureBranchMat;
    public Material headMat;
    public Material conflictMat;

    [Header("UI Reference (Auto-Detected if Empty)")]
    public TextMeshProUGUI statusAndCodeText;

    private bool repoPlaced = false;
    private Vector3 tableTopSurface = new Vector3(0f, 0.50f, 0f);
    private Quaternion repoRotation = Quaternion.identity;

    private string activeBranch = "main";
    private int commitCounter = 1;
    private bool hasConflict = false;

    private List<GameObject> spawnedObjects = new List<GameObject>();
    private List<GameObject> mainCommits = new List<GameObject>();
    private List<GameObject> featureCommits = new List<GameObject>();

    private GameObject headInstance;
    private GameObject activeConflictObject;
    private Transform currentHeadTarget;

    private float commitSpacingX = 0.12f;
    private float branchOffsetZ = 0.14f;

    private Camera activeCam;
    private float yaw = 0f;
    private float pitch = 26f;

    void Awake()
    {
        AutoCreateMaterialsIfNeeded();
    }

    void Start()
    {
        FixCameraForGameView();
        CreateSimulatedTableIfNeeded();
        AutoSetupAndLayoutUI();
        InitializeRepository(tableTopSurface, Quaternion.identity);
    }

    // Disables the yellow AR Camera Background override in Editor and frames the 3D table
    void FixCameraForGameView()
    {
        activeCam = Camera.main ?? Object.FindFirstObjectByType<Camera>();
        if (activeCam == null) return;

        // 1. Disable ARCameraBackground in the Unity Editor so it doesn't turn the screen yellow!
        ARCameraBackground arBg = activeCam.GetComponent<ARCameraBackground>();
        if (arBg != null) arBg.enabled = false;

        ARCameraManager arCamMgr = activeCam.GetComponent<ARCameraManager>();
        if (arCamMgr != null) arCamMgr.enabled = false;

        // 2. Disable TrackedPoseDriver so XR Simulation doesn't lock the camera at (0,0,0)
        foreach (MonoBehaviour mb in activeCam.GetComponents<MonoBehaviour>())
        {
            if (mb != null && mb.GetType().Name.Contains("PoseDriver"))
            {
                mb.enabled = false;
            }
        }

        // 3. Set a clean dark slate background in the Game View
        activeCam.clearFlags = CameraClearFlags.SolidColor;
        activeCam.backgroundColor = new Color(0.08f, 0.10f, 0.15f, 1f);
        activeCam.nearClipPlane = 0.01f;

        // 4. Reset parent offsets and place camera looking directly at the table
        if (activeCam.transform.parent != null)
        {
            activeCam.transform.parent.localPosition = Vector3.zero;
            activeCam.transform.parent.localRotation = Quaternion.identity;
            if (activeCam.transform.parent.parent != null)
            {
                activeCam.transform.parent.parent.position = Vector3.zero;
                activeCam.transform.parent.parent.rotation = Quaternion.identity;
            }
        }

        activeCam.transform.position = new Vector3(0f, 1.05f, -1.05f);
        activeCam.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    void LateUpdate()
    {
        if (activeCam == null)
        {
            FixCameraForGameView();
            return;
        }

        // Hold Right Mouse Button in Game View to look around
        if (Input.GetMouseButton(1))
        {
            yaw += Input.GetAxis("Mouse X") * 3f;
            pitch -= Input.GetAxis("Mouse Y") * 3f;
            pitch = Mathf.Clamp(pitch, 5f, 80f);
        }

        // Use WASD in Game View to move camera
        float moveSpeed = 0.8f * Time.deltaTime;
        Vector3 pos = activeCam.transform.position;
        if (Input.GetKey(KeyCode.W)) pos += activeCam.transform.forward * moveSpeed;
        if (Input.GetKey(KeyCode.S)) pos -= activeCam.transform.forward * moveSpeed;
        if (Input.GetKey(KeyCode.A)) pos -= activeCam.transform.right * moveSpeed;
        if (Input.GetKey(KeyCode.D)) pos += activeCam.transform.right * moveSpeed;

        activeCam.transform.position = pos;
        activeCam.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    void AutoSetupAndLayoutUI()
    {
        Canvas canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas != null)
        {
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
            }
        }

        if (statusAndCodeText == null)
        {
            GameObject txtObj = GameObject.Find("StatusAndCodeText") ?? GameObject.Find("StatusAndCode");
            if (txtObj != null) statusAndCodeText = txtObj.GetComponent<TextMeshProUGUI>();
        }

        if (statusAndCodeText != null)
        {
            RectTransform rt = statusAndCodeText.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(40f, -30f);
            rt.sizeDelta = new Vector2(980f, 220f);
            statusAndCodeText.fontSize = 24f;
            statusAndCodeText.color = Color.white;
            statusAndCodeText.raycastTarget = false;
        }

        string[] btnNames = { "Btn_Commit", "Btn_Branch", "Btn_Merge", "Btn_Conflict", "Btn_Reset" };
        string[] btnLabels = { "git commit", "git branch", "git merge", "Conflict Demo", "Reset AR" };
        UnityEngine.Events.UnityAction[] actions = { GitCommit, GitBranchOrSwitch, GitMerge, ToggleConflictDemo, ResetRepository };

        float startX = -520f;
        float spacingX = 260f;

        for (int i = 0; i < btnNames.Length; i++)
        {
            GameObject btnObj = GameObject.Find(btnNames[i]);
            if (btnObj != null)
            {
                RectTransform brt = btnObj.GetComponent<RectTransform>();
                if (brt != null)
                {
                    brt.anchorMin = new Vector2(0.5f, 0f);
                    brt.anchorMax = new Vector2(0.5f, 0f);
                    brt.pivot = new Vector2(0.5f, 0f);
                    brt.anchoredPosition = new Vector2(startX + (i * spacingX), 35f);
                    brt.sizeDelta = new Vector2(230f, 65f);
                }

                TextMeshProUGUI tmp = btnObj.GetComponentInChildren<TextMeshProUGUI>();
                if (tmp != null)
                {
                    tmp.text = btnLabels[i];
                    tmp.fontSize = 22f;
                }

                Button btn = btnObj.GetComponent<Button>();
                if (btn != null)
                {
                    btn.onClick.RemoveAllListeners();
                    btn.onClick.AddListener(actions[i]);
                }
            }
        }
    }

    void CreateSimulatedTableIfNeeded()
    {
        if (GameObject.Find("Tabletop") == null)
        {
            GameObject tableRoot = new GameObject("Simulated Bounding Box Table");

            GameObject tabletop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tabletop.name = "Tabletop";
            tabletop.transform.SetParent(tableRoot.transform);
            tabletop.transform.position = new Vector3(0f, 0.4632f, 0f);
            tabletop.transform.localScale = new Vector3(1.25f, 0.0735f, 0.85f);
            tabletop.GetComponent<Renderer>().material = CreateColoredMaterial(new Color(0.18f, 0.20f, 0.24f));

            Material legMat = CreateColoredMaterial(new Color(0.12f, 0.13f, 0.16f));
            Vector3[] legOffsets = new Vector3[]
            {
                new Vector3(-0.55f, 0.23f, -0.36f),
                new Vector3( 0.55f, 0.23f, -0.36f),
                new Vector3(-0.55f, 0.23f,  0.36f),
                new Vector3( 0.55f, 0.23f,  0.36f)
            };

            for (int i = 0; i < legOffsets.Length; i++)
            {
                GameObject leg = GameObject.CreatePrimitive(PrimitiveType.Cube);
                leg.name = $"Leg ({i + 1})";
                leg.transform.SetParent(tableRoot.transform);
                leg.transform.position = legOffsets[i];
                leg.transform.localScale = new Vector3(0.05f, 0.46f, 0.05f);
                leg.GetComponent<Renderer>().material = legMat;
            }
        }
    }

    void AutoCreateMaterialsIfNeeded()
    {
        if (mainBranchMat == null) mainBranchMat = CreateColoredMaterial(new Color(0f, 0.90f, 1f));
        if (featureBranchMat == null) featureBranchMat = CreateColoredMaterial(new Color(0.66f, 0.33f, 0.97f));
        if (headMat == null) headMat = CreateColoredMaterial(new Color(0.98f, 0.80f, 0.08f));
        if (conflictMat == null) conflictMat = CreateColoredMaterial(new Color(0.94f, 0.27f, 0.27f));
    }

    Material CreateColoredMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        Material mat = new Material(shader);
        mat.color = color;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        return mat;
    }

    void Update()
    {
        if (headInstance != null && currentHeadTarget != null)
        {
            float bobOffset = Mathf.Sin(Time.time * 4f) * 0.015f;
            headInstance.transform.position = currentHeadTarget.position + new Vector3(0, 0.11f + bobOffset, 0);
            headInstance.transform.Rotate(Vector3.up, 90f * Time.deltaTime, Space.World);
        }

        if (activeConflictObject != null)
        {
            activeConflictObject.transform.Rotate(Vector3.up, 120f * Time.deltaTime, Space.World);
        }
    }

    void InitializeRepository(Vector3 position, Quaternion rotation)
    {
        repoPlaced = true;
        Vector3 startPos = new Vector3(-0.36f, position.y + 0.05f, position.z - 0.06f);
        repoRotation = rotation;

        GameObject c1 = SpawnCommitNode(startPos, mainBranchMat, "C1");
        mainCommits.Add(c1);

        headInstance = SpawnHeadPointer(c1.transform.position + Vector3.up * 0.11f);
        spawnedObjects.Add(headInstance);
        currentHeadTarget = c1.transform;

        UpdateHUD(
            "Repo Ready in Game View! | Active Branch: main",
            "$ git init\n$ git commit -m \"C1\"\n> HEAD -> main (Click buttons at bottom of Game View)"
        );
    }

    public void GitCommit()
    {
        if (!repoPlaced || hasConflict) return;

        commitCounter++;
        string commitName = "C" + commitCounter;

        if (activeBranch == "main")
        {
            GameObject prevCommit = mainCommits[mainCommits.Count - 1];
            Vector3 nextPos = prevCommit.transform.position + new Vector3(commitSpacingX, 0, 0);

            GameObject newCommit = SpawnCommitNode(nextPos, mainBranchMat, commitName);
            CreateBranchRail(prevCommit.transform.position, nextPos, mainBranchMat);

            mainCommits.Add(newCommit);
            currentHeadTarget = newCommit.transform;

            UpdateHUD(
                $"Committed {commitName} on 'main'",
                $"$ git commit -m \"{commitName}\"\n> [main] HEAD moved to {commitName}"
            );
        }
        else
        {
            GameObject prevCommit = featureCommits.Count > 0
                ? featureCommits[featureCommits.Count - 1]
                : mainCommits[mainCommits.Count - 1];

            Vector3 nextPos = prevCommit.transform.position + new Vector3(commitSpacingX, 0, 0);

            GameObject newCommit = SpawnCommitNode(nextPos, featureBranchMat, commitName);
            CreateBranchRail(prevCommit.transform.position, nextPos, featureBranchMat);

            featureCommits.Add(newCommit);
            currentHeadTarget = newCommit.transform;

            UpdateHUD(
                $"Committed {commitName} on 'feature/auth'",
                $"$ git commit -m \"{commitName}\"\n> [feature/auth] HEAD moved to {commitName}"
            );
        }
    }

    public void GitBranchOrSwitch()
    {
        if (!repoPlaced || hasConflict) return;

        if (activeBranch == "main")
        {
            activeBranch = "feature/auth";

            if (featureCommits.Count == 0)
            {
                commitCounter++;
                string commitName = "C" + commitCounter;
                GameObject branchPoint = mainCommits[mainCommits.Count - 1];
                Vector3 branchPos = branchPoint.transform.position + new Vector3(commitSpacingX, 0, branchOffsetZ);

                GameObject newCommit = SpawnCommitNode(branchPos, featureBranchMat, commitName);
                CreateBranchRail(branchPoint.transform.position, branchPos, featureBranchMat);
                featureCommits.Add(newCommit);
                currentHeadTarget = newCommit.transform;

                UpdateHUD(
                    $"Branch created & switched to 'feature/auth' ({commitName})",
                    "$ git checkout -b feature/auth\n> Branch split from main. HEAD -> feature/auth"
                );
            }
            else
            {
                currentHeadTarget = featureCommits[featureCommits.Count - 1].transform;
                UpdateHUD(
                    "Switched to branch 'feature/auth'",
                    "$ git checkout feature/auth\n> HEAD -> feature/auth"
                );
            }
        }
        else
        {
            activeBranch = "main";
            currentHeadTarget = mainCommits[mainCommits.Count - 1].transform;

            UpdateHUD(
                "Switched back to branch 'main'",
                "$ git checkout main\n> HEAD -> main"
            );
        }
    }

    public void GitMerge()
    {
        if (!repoPlaced || hasConflict) return;

        if (featureCommits.Count == 0)
        {
            UpdateHUD("Nothing to merge! Click 'git branch' first.", "$ git merge feature/auth\n> Already up to date.");
            return;
        }

        activeBranch = "main";
        commitCounter++;
        string mergeCommitName = "M" + commitCounter;

        GameObject lastMain = mainCommits[mainCommits.Count - 1];
        GameObject lastFeature = featureCommits[featureCommits.Count - 1];

        float furthestX = Mathf.Max(lastMain.transform.position.x, lastFeature.transform.position.x);
        Vector3 mergePos = new Vector3(furthestX + commitSpacingX, lastMain.transform.position.y, lastMain.transform.position.z);

        GameObject mergeNode = SpawnCommitNode(mergePos, mainBranchMat, mergeCommitName);

        CreateBranchRail(lastMain.transform.position, mergePos, mainBranchMat);
        CreateBranchRail(lastFeature.transform.position, mergePos, featureBranchMat);

        mainCommits.Add(mergeNode);
        currentHeadTarget = mergeNode.transform;

        UpdateHUD(
            $"3-Way Merge Complete ({mergeCommitName})!",
            "$ git merge feature/auth\n> Merge made by 'ort' strategy. Both branch rails linked into main!"
        );
    }

    public void ToggleConflictDemo()
    {
        if (!repoPlaced) return;

        if (!hasConflict)
        {
            hasConflict = true;
            Vector3 conflictPos = currentHeadTarget.position + new Vector3(commitSpacingX * 0.5f, 0.02f, branchOffsetZ * 0.3f);
            activeConflictObject = SpawnConflictMarker(conflictPos);
            spawnedObjects.Add(activeConflictObject);

            UpdateHUD(
                "MERGE CONFLICT ACTIVE! Click 'Conflict Demo' again to resolve.",
                "$ git merge feature/auth\n<<<<<<< HEAD (main)\n  int timeout = 30;\n=======\n  int timeout = 60;\n>>>>>>> feature/auth"
            );
        }
        else
        {
            hasConflict = false;
            if (activeConflictObject != null)
            {
                spawnedObjects.Remove(activeConflictObject);
                Destroy(activeConflictObject);
            }

            UpdateHUD(
                "Conflict Resolved! You can now run 'git merge' or 'git commit'.",
                "$ git add .\n$ git commit -m \"Resolve conflict\"\n> Conflict marker cleared."
            );
        }
    }

    public void ResetRepository()
    {
        foreach (GameObject obj in spawnedObjects)
        {
            if (obj != null) Destroy(obj);
        }
        spawnedObjects.Clear();
        mainCommits.Clear();
        featureCommits.Clear();

        hasConflict = false;
        activeBranch = "main";
        commitCounter = 1;

        InitializeRepository(tableTopSurface, Quaternion.identity);
    }

    GameObject SpawnCommitNode(Vector3 pos, Material mat, string label)
    {
        GameObject node;
        if (commitNodePrefab != null)
        {
            node = Instantiate(commitNodePrefab, pos, repoRotation);
        }
        else
        {
            node = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            node.transform.position = pos;
            node.transform.rotation = repoRotation;
            node.transform.localScale = new Vector3(0.075f, 0.075f, 0.075f);
            Destroy(node.GetComponent<Collider>());
        }

        if (mat != null && node.GetComponent<Renderer>() != null)
            node.GetComponent<Renderer>().material = mat;

        GameObject textObj = new GameObject("Label_" + label);
        textObj.transform.SetParent(node.transform);
        textObj.transform.localPosition = new Vector3(0f, 0.85f, 0f);
        textObj.transform.localScale = new Vector3(0.15f, 0.15f, 0.15f);
        TextMeshPro tmp = textObj.AddComponent<TextMeshPro>();
        tmp.text = label;
        tmp.fontSize = 18;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;

        node.name = label;
        spawnedObjects.Add(node);
        return node;
    }

    GameObject SpawnHeadPointer(Vector3 pos)
    {
        GameObject pointer;
        if (headPointerPrefab != null)
        {
            pointer = Instantiate(headPointerPrefab, pos, Quaternion.Euler(45f, 0f, 45f));
        }
        else
        {
            pointer = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pointer.transform.position = pos;
            pointer.transform.rotation = Quaternion.Euler(45f, 0f, 45f);
            pointer.transform.localScale = new Vector3(0.05f, 0.05f, 0.05f);
            Destroy(pointer.GetComponent<Collider>());
        }

        if (headMat != null && pointer.GetComponent<Renderer>() != null)
            pointer.GetComponent<Renderer>().material = headMat;

        pointer.name = "HEAD_Pointer";
        return pointer;
    }

    GameObject SpawnConflictMarker(Vector3 pos)
    {
        GameObject marker;
        if (conflictMarkerPrefab != null)
        {
            marker = Instantiate(conflictMarkerPrefab, pos, Quaternion.Euler(35f, 45f, 35f));
        }
        else
        {
            marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.transform.position = pos;
            marker.transform.rotation = Quaternion.Euler(35f, 45f, 35f);
            marker.transform.localScale = new Vector3(0.085f, 0.085f, 0.085f);
            Destroy(marker.GetComponent<Collider>());
        }

        if (conflictMat != null && marker.GetComponent<Renderer>() != null)
            marker.GetComponent<Renderer>().material = conflictMat;

        marker.name = "MergeConflict_Marker";
        return marker;
    }

    void CreateBranchRail(Vector3 startPos, Vector3 endPos, Material mat)
    {
        GameObject rail = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(rail.GetComponent<Collider>());

        Vector3 centerPos = (startPos + endPos) / 2f;
        Vector3 direction = endPos - startPos;
        float distance = direction.magnitude;

        rail.transform.position = centerPos;
        rail.transform.up = direction.normalized;
        rail.transform.localScale = new Vector3(0.018f, distance * 0.5f, 0.018f);

        if (mat != null) rail.GetComponent<Renderer>().material = mat;

        spawnedObjects.Add(rail);
    }

    void UpdateHUD(string statusLine, string codeSnippet)
    {
        if (statusAndCodeText != null)
        {
            statusAndCodeText.text = $"<b>{statusLine}</b>\n\n<color=#38BDF8>{codeSnippet}</color>";
        }
    }
}