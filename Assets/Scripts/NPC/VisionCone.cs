using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class VisionCone : MonoBehaviour
{
    [Header("Vision")]
    [SerializeField]
    private float visionRange = 5f;

    [SerializeField, Range(1f, 360f)]
    private float visionAngle = 90f;

    [SerializeField, Min(8)]
    private int segments = 48;

    [Header("Detection")]
    [SerializeField]
    private LayerMask targetMask;

    [SerializeField]
    private LayerMask obstacleMask;

    [SerializeField]
    private bool checkObstacles = true;

    [SerializeField]
    private bool detectTargets = true;

    [Header("Visualization")]
    [SerializeField]
    private bool showCone = true;

    [SerializeField]
    private bool showBoundary = true;

    [SerializeField]
    private Color coneColor = new Color(1f, 0f, 0f, 0.15f);

    [SerializeField]
    private Color boundaryColor = Color.red;

    [SerializeField, Min(0.001f)]
    private float boundaryWidth = 0.04f;

    [Header("Debug")]
    [SerializeField]
    private bool logDetection = true;

    [SerializeField]
    private bool drawGizmos = true;

    [SerializeField]
    private bool logEveryFrame = false;

    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;

    private Mesh coneMesh;

    private readonly List<LineRenderer> boundaryLines =
        new List<LineRenderer>();

    /*
     * Boundary children are matched by name so that a domain reload,
     * which wipes boundaryLines without touching the GameObjects it
     * referred to, can re-adopt the existing ones instead of creating
     * a fresh set every recompile.
     */

    private static readonly string[] BoundaryNames =
    {
        "Vision Left Boundary",
        "Vision Right Boundary",
        "Vision Far Boundary"
    };

    /*
     * One material shared by all three lines, rather than one each.
     */

    private Material boundaryMaterial;

    private readonly HashSet<Transform> detectedTargets =
        new HashSet<Transform>();

    private readonly HashSet<Transform> previousTargets =
        new HashSet<Transform>();

    private readonly Collider[] overlapResults =
        new Collider[128];

    public IReadOnlyCollection<Transform> DetectedTargets =>
        detectedTargets;

    public float VisionRange => visionRange;

    public float VisionAngle => visionAngle;


    // =========================================================
    // UNITY
    // =========================================================

    private void Awake()
    {
        Initialize();
    }

    private void OnEnable()
    {
        Initialize();
    }

    private void Update()
    {
        if (!Application.isPlaying)
            return;

        if (!detectTargets)
            return;

        ScanForTargets();
    }

    private void OnValidate()
    {
        visionRange = Mathf.Max(0.01f, visionRange);

        visionAngle =
            Mathf.Clamp(
                visionAngle,
                1f,
                360f
            );

        segments =
            Mathf.Max(
                8,
                segments
            );

        boundaryWidth =
            Mathf.Max(
                0.001f,
                boundaryWidth
            );

        /*
         * Rebuilding here directly is not safe. Initialize destroys the
         * old mesh and any surplus boundary objects, and Unity forbids
         * DestroyImmediate while OnValidate is on the stack. Deferring
         * by one editor tick puts that work somewhere it is legal.
         */

#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall += DeferredInitialize;
#else
        Initialize();
#endif
    }

#if UNITY_EDITOR
    private void DeferredInitialize()
    {
        UnityEditor.EditorApplication.delayCall -= DeferredInitialize;

        /*
         * The component may have been deleted between the validate and
         * this callback. The fake-null comparison is the point here.
         */

        if (this == null)
            return;

        Initialize();
    }
#endif

    private void OnDestroy()
    {
        CleanupMesh();

        CleanupBoundaryObjects();

        CleanupBoundaryMaterial();
    }


    // =========================================================
    // INITIALIZATION
    // =========================================================

    private void Initialize()
    {
        meshFilter =
            GetComponent<MeshFilter>();

        meshRenderer =
            GetComponent<MeshRenderer>();

        if (meshFilter == null ||
            meshRenderer == null)
        {
            return;
        }

        GenerateCone();

        SetupMaterial();

        SetupBoundaryLines();

        UpdateVisualization();
    }


    // =========================================================
    // CONE GENERATION
    // =========================================================

    private void GenerateCone()
    {
        if (meshFilter == null)
            return;

        CleanupMesh();

        coneMesh =
            new Mesh();

        coneMesh.name =
            "Vision Cone";

        /*
         * Generated every rebuild, so it must never be written into
         * the scene file.
         */

        coneMesh.hideFlags =
            HideFlags.DontSave;

        List<Vector3> vertices =
            new List<Vector3>();

        List<int> triangles =
            new List<int>();

        float halfAngle =
            visionAngle * 0.5f;

        float angleStep =
            visionAngle / segments;

        float halfAngleRadians =
            halfAngle * Mathf.Deg2Rad;

        /*
         * Radius at the end of the cone.
         *
         * range
         *    |
         *    |\
         *    | \
         *    |  \
         *    |   \
         *    |____\
         *
         */

        float radius =
            Mathf.Tan(
                halfAngleRadians
            ) * visionRange;


        // =====================================================
        // CENTER
        // =====================================================

        int centerIndex =
            vertices.Count;

        vertices.Add(
            Vector3.zero
        );


        // =====================================================
        // ARC
        // =====================================================

        int arcStart =
            vertices.Count;

        for (int i = 0; i <= segments; i++)
        {
            float angle =
                -halfAngle +
                angleStep * i;

            float radians =
                angle * Mathf.Deg2Rad;

            float x =
                Mathf.Sin(radians) *
                radius;

            float z =
                Mathf.Cos(radians) *
                visionRange;

            vertices.Add(
                new Vector3(
                    x,
                    0f,
                    z
                )
            );
        }


        // =====================================================
        // FLOOR / FILLED VISION AREA
        // =====================================================

        for (int i = 0; i < segments; i++)
        {
            int current =
                arcStart + i;

            int next =
                arcStart + i + 1;

            /*
             * First side
             */

            triangles.Add(centerIndex);
            triangles.Add(current);
            triangles.Add(next);

            /*
             * Reverse side.
             *
             * This is important because it makes the
             * vision area visible from both sides.
             */

            triangles.Add(centerIndex);
            triangles.Add(next);
            triangles.Add(current);
        }


        // =====================================================
        // OUTER WALL
        // =====================================================

        int outerBottomStart =
            vertices.Count;

        for (int i = 0; i <= segments; i++)
        {
            float angle =
                -halfAngle +
                angleStep * i;

            float radians =
                angle * Mathf.Deg2Rad;

            float x =
                Mathf.Sin(radians) *
                radius;

            float z =
                Mathf.Cos(radians) *
                visionRange;

            vertices.Add(
                new Vector3(
                    x,
                    0.15f,
                    z
                )
            );
        }


        // =====================================================
        // WALL
        // =====================================================

        for (int i = 0; i < segments; i++)
        {
            int bottomA =
                arcStart + i;

            int bottomB =
                arcStart + i + 1;

            int topA =
                outerBottomStart + i;

            int topB =
                outerBottomStart + i + 1;


            // Side A

            triangles.Add(bottomA);
            triangles.Add(topA);
            triangles.Add(topB);

            triangles.Add(bottomA);
            triangles.Add(topB);
            triangles.Add(bottomB);


            // Reverse side

            triangles.Add(bottomA);
            triangles.Add(topB);
            triangles.Add(topA);

            triangles.Add(bottomA);
            triangles.Add(bottomB);
            triangles.Add(topB);
        }


        // =====================================================
        // LEFT / RIGHT WALL
        // =====================================================

        int leftBottom =
            arcStart;

        int rightBottom =
            arcStart + segments;

        int leftTop =
            outerBottomStart;

        int rightTop =
            outerBottomStart + segments;


        // LEFT

        triangles.Add(leftBottom);
        triangles.Add(leftTop);
        triangles.Add(rightTop);

        triangles.Add(leftBottom);
        triangles.Add(rightTop);
        triangles.Add(rightBottom);

        triangles.Add(leftBottom);
        triangles.Add(rightTop);
        triangles.Add(leftTop);

        triangles.Add(leftBottom);
        triangles.Add(rightBottom);
        triangles.Add(rightTop);


        // =====================================================
        // FINALIZE
        // =====================================================

        coneMesh.SetVertices(
            vertices
        );

        coneMesh.SetTriangles(
            triangles,
            0
        );

        coneMesh.RecalculateNormals();
        coneMesh.RecalculateBounds();

        meshFilter.sharedMesh =
            coneMesh;
    }


    // =========================================================
    // MATERIAL
    // =========================================================

    private void SetupMaterial()
    {
        if (meshRenderer == null)
            return;

        Material material =
            meshRenderer.sharedMaterial;

        if (material == null)
        {
            Shader shader =
                Shader.Find(
                    "Universal Render Pipeline/Unlit"
                );

            if (shader == null)
            {
                shader =
                    Shader.Find(
                        "Unlit/Color"
                    );
            }

            if (shader != null)
            {
                material =
                    new Material(shader);

                material.name =
                    "Vision Cone (generated)";

                material.hideFlags =
                    HideFlags.DontSave;

                meshRenderer.sharedMaterial =
                    material;
            }
        }

        if (material == null)
            return;


        material.color =
            coneColor;


        /*
         * URP
         */

        if (material.HasProperty("_Surface"))
        {
            material.SetFloat(
                "_Surface",
                1f
            );
        }

        /*
         * Transparent mode
         */

        if (material.HasProperty("_Blend"))
        {
            material.SetFloat(
                "_Blend",
                0f
            );
        }

        /*
         * IMPORTANT:
         *
         * Disable back-face culling.
         *
         * The cone will be visible from every angle.
         */

        if (material.HasProperty("_Cull"))
        {
            material.SetFloat(
                "_Cull",
                0f
            );
        }

        if (material.HasProperty("_ZWrite"))
        {
            material.SetFloat(
                "_ZWrite",
                0f
            );
        }

        material.renderQueue =
            3000;
    }


    // =========================================================
    // BOUNDARY
    // =========================================================

    private void SetupBoundaryLines()
    {
        ReconcileBoundaryLines();

        if (!showBoundary)
        {
            SetBoundaryVisible(false);
            return;
        }

        UpdateBoundaryLines();
    }


    /// <summary>
    /// Brings the three boundary children into line with what actually
    /// exists in the scene.
    ///
    /// The old version simply created lines until the list held three.
    /// Because boundaryLines is not serialized, every domain reload
    /// emptied it while leaving the previous GameObjects parented under
    /// this transform, so each recompile added three more forever. The
    /// fix is to adopt what is already there and destroy the surplus.
    /// </summary>
    private void ReconcileBoundaryLines()
    {
        LineRenderer[] slots =
            new LineRenderer[BoundaryNames.Length];

        /*
         * Backwards, because surplus children get destroyed as we go.
         */

        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);

            int index =
                System.Array.IndexOf(BoundaryNames, child.name);

            if (index < 0)
                continue;

            LineRenderer line =
                child.GetComponent<LineRenderer>();

            if (line == null)
                continue;

            if (slots[index] == null)
            {
                slots[index] = line;

                MarkAsGenerated(child.gameObject);
            }
            else
            {
                DestroyGenerated(child.gameObject);
            }
        }

        boundaryLines.Clear();

        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == null)
            {
                slots[i] = CreateBoundaryLine(i);
            }

            ConfigureBoundaryLine(slots[i]);

            boundaryLines.Add(slots[i]);
        }
    }


    private LineRenderer CreateBoundaryLine(
        int index)
    {
        GameObject obj =
            new GameObject(BoundaryNames[index]);

        obj.transform.SetParent(
            transform,
            false
        );

        MarkAsGenerated(obj);

        return obj.AddComponent<LineRenderer>();
    }


    private void ConfigureBoundaryLine(
        LineRenderer line)
    {
        if (line == null)
            return;

        line.useWorldSpace =
            false;

        line.positionCount =
            2;

        line.startWidth =
            boundaryWidth;

        line.endWidth =
            boundaryWidth;

        line.startColor =
            boundaryColor;

        line.endColor =
            boundaryColor;

        /*
         * sharedMaterial, never material. Reading .material in the
         * editor instantiates a private copy on the spot, which was a
         * second source of the material pile-up in the scene file.
         */

        line.sharedMaterial =
            GetBoundaryMaterial();
    }


    /// <summary>
    /// One material for every boundary line on this cone, created once.
    /// </summary>
    private Material GetBoundaryMaterial()
    {
        if (boundaryMaterial != null)
        {
            boundaryMaterial.color = boundaryColor;

            return boundaryMaterial;
        }

        Shader shader =
            Shader.Find(
                "Universal Render Pipeline/Unlit"
            );

        if (shader == null)
        {
            shader =
                Shader.Find(
                    "Unlit/Color"
                );
        }

        if (shader == null)
            return null;

        boundaryMaterial =
            new Material(shader);

        boundaryMaterial.name =
            "Vision Boundary (generated)";

        /*
         * DontSave keeps generated throwaways out of the scene file
         * entirely, so nothing can accumulate there again.
         */

        boundaryMaterial.hideFlags =
            HideFlags.DontSave;

        boundaryMaterial.color =
            boundaryColor;

        return boundaryMaterial;
    }


    private static void MarkAsGenerated(GameObject obj)
    {
        obj.hideFlags = HideFlags.DontSave;
    }


    private static void DestroyGenerated(Object target)
    {
        if (target == null)
            return;

        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }


    private void UpdateBoundaryLines()
    {
        if (boundaryLines.Count < 3)
            return;

        float halfAngle =
            visionAngle * 0.5f;

        float radians =
            halfAngle *
            Mathf.Deg2Rad;

        float radius =
            Mathf.Tan(radians) *
            visionRange;


        Vector3 left =
            new Vector3(
                -Mathf.Sin(radians) *
                radius,

                0.05f,

                Mathf.Cos(radians) *
                visionRange
            );


        Vector3 right =
            new Vector3(
                Mathf.Sin(radians) *
                radius,

                0.05f,

                Mathf.Cos(radians) *
                visionRange
            );


        // LEFT

        boundaryLines[0].SetPosition(
            0,
            Vector3.zero
        );

        boundaryLines[0].SetPosition(
            1,
            left
        );


        // RIGHT

        boundaryLines[1].SetPosition(
            0,
            Vector3.zero
        );

        boundaryLines[1].SetPosition(
            1,
            right
        );


        // FAR

        boundaryLines[2].SetPosition(
            0,
            left
        );

        boundaryLines[2].SetPosition(
            1,
            right
        );


        foreach (
            LineRenderer line
            in boundaryLines)
        {
            if (line == null)
                continue;

            line.startWidth =
                boundaryWidth;

            line.endWidth =
                boundaryWidth;

            line.startColor =
                boundaryColor;

            line.endColor =
                boundaryColor;
        }
    }


    // =========================================================
    // DETECTION
    // =========================================================

    private void ScanForTargets()
    {
        int count =
            Physics.OverlapSphereNonAlloc(
                transform.position,
                visionRange,
                overlapResults,
                targetMask
            );

        detectedTargets.Clear();


        for (int i = 0; i < count; i++)
        {
            Collider collider =
                overlapResults[i];

            if (collider == null)
                continue;

            Transform target =
                collider.transform;

            /*
             * If target is a child of the object,
             * use the root object.
             */

            if (target.root != transform.root)
            {
                target =
                    target.root;
            }

            if (IsTargetInsideCone(target))
            {
                detectedTargets.Add(
                    target
                );
            }
        }


        HandleDetectionChanges();


        if (logEveryFrame &&
            detectedTargets.Count > 0)
        {
            foreach (
                Transform detected
                in detectedTargets)
            {
                Debug.Log(
                    $"[VisionCone] {name} " +
                    $"currently sees {detected.name}",
                    this
                );
            }
        }
    }


    // =========================================================
    // CONE TEST
    // =========================================================

    public bool IsTargetInsideCone(
        Transform target)
    {
        if (target == null)
            return false;


        Vector3 direction =
            target.position -
            transform.position;


        float distance =
            direction.magnitude;


        // -----------------------------------------------------
        // RANGE
        // -----------------------------------------------------

        if (distance > visionRange)
            return false;


        // -----------------------------------------------------
        // ANGLE
        // -----------------------------------------------------

        float angle =
            Vector3.Angle(
                transform.forward,
                direction
            );


        if (angle >
            visionAngle * 0.5f)
        {
            return false;
        }


        // -----------------------------------------------------
        // OBSTACLE
        // -----------------------------------------------------

        if (checkObstacles)
        {
            if (Physics.Raycast(
                    transform.position,
                    direction.normalized,
                    out RaycastHit hit,
                    distance,
                    obstacleMask))
            {
                return false;
            }
        }


        return true;
    }


    // =========================================================
    // DETECTION EVENTS
    // =========================================================

    private void HandleDetectionChanges()
    {
        /*
         * NEW TARGETS
         */

        foreach (
            Transform target
            in detectedTargets)
        {
            if (!previousTargets.Contains(target))
            {
                OnTargetEnter(
                    target
                );
            }
        }


        /*
         * LOST TARGETS
         */

        foreach (
            Transform target
            in previousTargets)
        {
            if (!detectedTargets.Contains(target))
            {
                OnTargetExit(
                    target
                );
            }
        }


        previousTargets.Clear();

        foreach (
            Transform target
            in detectedTargets)
        {
            previousTargets.Add(
                target
            );
        }
    }


    private void OnTargetEnter(
        Transform target)
    {
        if (!logDetection)
            return;


        float distance =
            Vector3.Distance(
                transform.position,
                target.position
            );


        float angle =
            Vector3.Angle(
                transform.forward,
                target.position -
                transform.position
            );


        Debug.Log(
            $"[VisionCone] {name} " +
            $"DETECTED '{target.name}' " +
            $"| Distance: {distance:F2} " +
            $"| Angle: {angle:F1}°",
            this
        );
    }


    private void OnTargetExit(
        Transform target)
    {
        if (!logDetection)
            return;


        Debug.Log(
            $"[VisionCone] {name} " +
            $"LOST '{target.name}'",
            this
        );
    }


    // =========================================================
    // VISUAL CONTROL
    // =========================================================

    public void SetConeVisible(
        bool visible)
    {
        showCone =
            visible;

        UpdateVisualization();
    }


    private void UpdateVisualization()
    {
        if (meshRenderer != null)
        {
            meshRenderer.enabled =
                showCone;
        }


        SetBoundaryVisible(
            showCone &&
            showBoundary
        );
    }


    private void SetBoundaryVisible(
        bool visible)
    {
        foreach (
            LineRenderer line
            in boundaryLines)
        {
            if (line != null)
            {
                line.enabled =
                    visible;
            }
        }
    }


    // =========================================================
    // CLEANUP
    // =========================================================

    private void CleanupMesh()
    {
        if (coneMesh == null)
            return;

        /*
         * Previously this only destroyed in play mode and simply
         * dropped the reference in the editor, orphaning a Mesh on
         * every rebuild.
         */

        DestroyGenerated(coneMesh);

        coneMesh =
            null;
    }


    private void CleanupBoundaryObjects()
    {
        foreach (
            LineRenderer line
            in boundaryLines)
        {
            if (line == null)
                continue;

            DestroyGenerated(
                line.gameObject
            );
        }

        boundaryLines.Clear();
    }


    private void CleanupBoundaryMaterial()
    {
        if (boundaryMaterial == null)
            return;

        DestroyGenerated(boundaryMaterial);

        boundaryMaterial = null;
    }


    // =========================================================
    // GIZMOS
    // =========================================================

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos)
            return;


        Gizmos.color =
            boundaryColor;


        Vector3 origin =
            transform.position;


        float halfAngle =
            visionAngle * 0.5f;


        Vector3 left =
            Quaternion.Euler(
                0f,
                -halfAngle,
                0f
            ) *
            transform.forward;


        Vector3 right =
            Quaternion.Euler(
                0f,
                halfAngle,
                0f
            ) *
            transform.forward;


        Gizmos.DrawRay(
            origin,
            left *
            visionRange
        );


        Gizmos.DrawRay(
            origin,
            right *
            visionRange
        );


        Gizmos.DrawRay(
            origin,
            transform.forward *
            visionRange
        );


        /*
         * Draw approximate range arc.
         */

        Vector3 previous =
            origin +
            left *
            visionRange;


        int gizmoSegments =
            Mathf.Min(
                segments,
                32
            );


        for (int i = 1;
             i <= gizmoSegments;
             i++)
        {
            float t =
                (float)i /
                gizmoSegments;


            float angle =
                Mathf.Lerp(
                    -halfAngle,
                    halfAngle,
                    t
                );


            Vector3 direction =
                Quaternion.Euler(
                    0f,
                    angle,
                    0f
                ) *
                transform.forward;


            Vector3 current =
                origin +
                direction *
                visionRange;


            Gizmos.DrawLine(
                previous,
                current
            );


            previous =
                current;
        }
    }
}