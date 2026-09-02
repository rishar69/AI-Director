using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Draws a VisionSense's cone as a flat sector on the ground and tints
/// it by how aware the creature currently is.
///
/// This is a debug view, not something the player is meant to see. It
/// earns its place because awareness is otherwise completely invisible -
/// watching the cone go from blue to orange to red as you edge around a
/// corner is the fastest way to tune falloff values.
///
/// Separate from VisionSense on purpose: the sense should not care
/// whether anything is drawing it, and deleting this component should
/// never change behaviour.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class VisionConeVisualizer : MonoBehaviour
{
    // =========================================================
    // INSPECTOR
    // =========================================================

    [Header("Source")]
    [SerializeField]
    private VisionSense visionSense;

    [SerializeField]
    private PerceptionSystem perception;


    [Header("Shape")]
    [SerializeField, Min(8)]
    private int segments = 48;

    /// <summary>
    /// Lifts the sector off the floor so it does not z-fight.
    /// </summary>
    [SerializeField]
    private float heightOffset = 0.05f;


    [Header("Colours")]
    [SerializeField]
    private Color unawareColor = new Color(0.3f, 0.7f, 1f, 0.10f);

    [SerializeField]
    private Color suspiciousColor = new Color(1f, 0.9f, 0.2f, 0.14f);

    [SerializeField]
    private Color alertedColor = new Color(1f, 0.5f, 0.1f, 0.18f);

    [SerializeField]
    private Color confirmedColor = new Color(1f, 0.1f, 0.1f, 0.24f);


    // =========================================================
    // STATE
    // =========================================================

    private MeshFilter meshFilter;

    private MeshRenderer meshRenderer;

    private Mesh mesh;

    private Material material;

    private MaterialPropertyBlock propertyBlock;

    /*
     * Cached so the mesh is only rebuilt when the cone actually
     * changes shape, not every frame.
     */

    private float builtRange = -1f;

    private float builtAngle = -1f;

    private int builtSegments = -1;

    private float builtHeight = float.NaN;

    private static readonly int BaseColorId =
        Shader.PropertyToID("_BaseColor");

    private static readonly int ColorId =
        Shader.PropertyToID("_Color");


    // =========================================================
    // UNITY
    // =========================================================

    private void OnEnable()
    {
        meshFilter = GetComponent<MeshFilter>();

        meshRenderer = GetComponent<MeshRenderer>();

        if (visionSense == null)
        {
            visionSense = GetComponentInParent<VisionSense>();
        }

        if (perception == null)
        {
            perception = GetComponentInParent<PerceptionSystem>();
        }

        EnsureMaterial();

        Rebuild();
    }


    private void OnDisable()
    {
        Cleanup();
    }


    private void LateUpdate()
    {
        if (visionSense == null)
            return;

        Rebuild();

        ApplyTint();
    }


    private void OnValidate()
    {
        segments = Mathf.Max(8, segments);

        builtRange = -1f;
    }


    // =========================================================
    // MESH
    // =========================================================

    private void Rebuild()
    {
        if (visionSense == null)
            return;

        float range = visionSense.Range;

        float angle = visionSense.Angle;

        bool unchanged =
            Mathf.Approximately(range, builtRange) &&
            Mathf.Approximately(angle, builtAngle) &&
            segments == builtSegments &&
            Mathf.Approximately(heightOffset, builtHeight);

        if (unchanged && mesh != null)
            return;

        builtRange = range;
        builtAngle = angle;
        builtSegments = segments;
        builtHeight = heightOffset;

        BuildSector(range, angle);
    }


    /// <summary>
    /// Builds a true circular sector.
    ///
    /// Every point on the arc sits exactly `range` metres from the
    /// origin, which is what the sense actually tests. A tangent-based
    /// width would blow up to infinity at 180 degrees and invert beyond
    /// it, so the drawn cone would stop matching the tested cone at
    /// exactly the wide angles a xenomorph wants.
    /// </summary>
    private void BuildSector(
        float range,
        float angle)
    {
        if (mesh == null)
        {
            mesh = new Mesh
            {
                name = "Vision Sector",
                hideFlags = HideFlags.DontSave
            };
        }

        mesh.Clear();

        int vertexCount = segments + 2;

        Vector3[] vertices = new Vector3[vertexCount];

        /*
         * Two triangles per segment - one per winding - so the sector
         * is visible from above and below without needing a two sided
         * shader variant.
         */

        int[] triangles = new int[segments * 6];

        vertices[0] = new Vector3(0f, heightOffset, 0f);

        float halfAngle = angle * 0.5f;

        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;

            float a = Mathf.Lerp(-halfAngle, halfAngle, t);

            float radians = a * Mathf.Deg2Rad;

            vertices[i + 1] =
                new Vector3(
                    Mathf.Sin(radians) * range,
                    heightOffset,
                    Mathf.Cos(radians) * range
                );
        }

        for (int i = 0; i < segments; i++)
        {
            int t = i * 6;

            int current = i + 1;

            int next = i + 2;

            triangles[t + 0] = 0;
            triangles[t + 1] = current;
            triangles[t + 2] = next;

            triangles[t + 3] = 0;
            triangles[t + 4] = next;
            triangles[t + 5] = current;
        }

        mesh.vertices = vertices;

        mesh.triangles = triangles;

        mesh.RecalculateNormals();

        mesh.RecalculateBounds();

        meshFilter.sharedMesh = mesh;
    }


    // =========================================================
    // MATERIAL
    // =========================================================

    private void EnsureMaterial()
    {
        if (material != null)
            return;

        Shader shader =
            Shader.Find("Universal Render Pipeline/Unlit");

        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        if (shader == null)
            return;

        material = new Material(shader)
        {
            name = "Vision Cone (generated)",
            hideFlags = HideFlags.DontSave
        };

        ConfigureTransparency(material);

        meshRenderer.sharedMaterial = material;
    }


    private static void ConfigureTransparency(Material target)
    {
        if (target.HasProperty("_Surface"))
        {
            target.SetFloat("_Surface", 1f);
        }

        if (target.HasProperty("_Blend"))
        {
            target.SetFloat("_Blend", 0f);
        }

        if (target.HasProperty("_SrcBlend"))
        {
            target.SetFloat(
                "_SrcBlend",
                (float)BlendMode.SrcAlpha
            );
        }

        if (target.HasProperty("_DstBlend"))
        {
            target.SetFloat(
                "_DstBlend",
                (float)BlendMode.OneMinusSrcAlpha
            );
        }

        if (target.HasProperty("_ZWrite"))
        {
            target.SetFloat("_ZWrite", 0f);
        }

        /*
         * No back face culling - the cone reads correctly from any
         * camera angle, including from underneath.
         */

        if (target.HasProperty("_Cull"))
        {
            target.SetFloat("_Cull", 0f);
        }

        target.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

        target.renderQueue = (int)RenderQueue.Transparent;
    }


    // =========================================================
    // TINT
    // =========================================================

    private void ApplyTint()
    {
        if (meshRenderer == null)
            return;

        Color tint = ColorForAwareness();

        propertyBlock ??= new MaterialPropertyBlock();

        meshRenderer.GetPropertyBlock(propertyBlock);

        /*
         * A property block avoids instantiating a material per
         * creature, which would leak one material per alien per play
         * session in the editor.
         */

        propertyBlock.SetColor(BaseColorId, tint);

        propertyBlock.SetColor(ColorId, tint);

        meshRenderer.SetPropertyBlock(propertyBlock);
    }


    private Color ColorForAwareness()
    {
        if (perception == null)
            return unawareColor;

        switch (perception.Level)
        {
            case AwarenessLevel.Confirmed:
                return confirmedColor;

            case AwarenessLevel.Alerted:
                return alertedColor;

            case AwarenessLevel.Suspicious:
                return suspiciousColor;

            default:
                return unawareColor;
        }
    }


    // =========================================================
    // CLEANUP
    // =========================================================

    private void Cleanup()
    {
        if (mesh != null)
        {
            DestroyAsset(mesh);

            mesh = null;
        }

        if (material != null)
        {
            DestroyAsset(material);

            material = null;
        }

        builtRange = -1f;
    }


    private static void DestroyAsset(Object asset)
    {
        if (Application.isPlaying)
        {
            Destroy(asset);
        }
        else
        {
            DestroyImmediate(asset);
        }
    }
}
