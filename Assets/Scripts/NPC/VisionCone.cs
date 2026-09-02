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

        Initialize();
    }

    private void OnDestroy()
    {
        CleanupMesh();

        if (Application.isPlaying)
        {
            CleanupBoundaryObjects();
        }
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
        if (!showBoundary)
        {
            SetBoundaryVisible(false);
            return;
        }

        while (boundaryLines.Count < 3)
        {
            CreateBoundaryLine(
                boundaryLines.Count
            );
        }

        UpdateBoundaryLines();
    }


    private void CreateBoundaryLine(
        int index)
    {
        GameObject obj =
            new GameObject(
                index == 0
                    ? "Vision Left Boundary"
                    : index == 1
                        ? "Vision Right Boundary"
                        : "Vision Far Boundary"
            );

        obj.transform.SetParent(
            transform,
            false
        );

        LineRenderer line =
            obj.AddComponent<LineRenderer>();

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

        line.material =
            CreateBoundaryMaterial();

        boundaryLines.Add(
            line
        );
    }


    private Material CreateBoundaryMaterial()
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

        Material material =
            new Material(shader);

        material.color =
            boundaryColor;

        return material;
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


        if (Application.isPlaying)
        {
            Destroy(
                coneMesh
            );
        }

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

            Destroy(
                line.gameObject
            );
        }

        boundaryLines.Clear();
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