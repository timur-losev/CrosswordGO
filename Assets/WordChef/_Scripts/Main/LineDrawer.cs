using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LineDrawer : MonoBehaviour {

    public List<Vector3> letterPositions = new List<Vector3>();
    public List<int> currentIndexes = new List<int>();
    public List<Vector3> points = new List<Vector3>();
    public List<Vector3> positions = new List<Vector3>();
    public TextPreview textPreview;

    private LineRenderer lineRenderer;
    public LineRenderer shimmerLineRenderer;
    private Vector3 mousePoint;
    public static LineDrawer instance;

    private bool isDragging;
    private float RADIUS = 0.35f;
    [Range(0.05f, 1f)]
    public float nonMatchRadiusCoef = 0.2f;
    private readonly HashSet<char> allowedNextLetters = new HashSet<char>();

    [Header("Gold Line Visuals")]
    public Texture2D goldLineTexture;
    public Material goldLineMaterial;
    public Texture2D shimmerTexture;
    public Material shimmerLineMaterial;
    public Shader lineShader;
    public Shader additiveShader;
    public float goldScrollSpeed = 0.15f;
    [Range(0.05f, 2f)]
    public float shimmerWidthCoef = 0.65f;
    public float shimmerScrollSpeed = 0.6f;
    public float shimmerTiling = 2f;

    [Header("Stars")]
    public Sprite starSprite;
    public Material starMaterial;
    public Color starColor = new Color(1f, 0.92f, 0.65f, 1f);
    public float starSpacing = 0.35f;
    [Range(0f, 1f)]
    public float starSpacingJitter = 0.35f;
    public float starSize = 0.2f;
    public float starAlphaMin = 0.35f;
    public float starAlphaMax = 0.9f;
    public float starFlickerSpeed = 1.2f;
    private readonly List<SpriteRenderer> starRenderers = new List<SpriteRenderer>();
    private readonly List<float> starPhases = new List<float>();
    private readonly List<float> starDistances = new List<float>();
    private float lastStarLineLength = -1f;
    private int lastStarCount = 0;
    private bool starLayoutDirty = true;
    private int starSeed = 0;
    private Transform starRoot;
    private float shimmerOffset;
    private float goldOffset;

    private void Awake()
    {
        instance = this;
    }

    private void Start()
    {
        lineRenderer = GetComponent<LineRenderer>();
        if (lineRenderer != null)
        {
            lineRenderer.sortingLayerName = "MyLineRenderer";
        }
        SetupLineRenderers();
    }

    private void Update()
    {
        if (DialogController.instance != null && DialogController.instance.IsDialogShowing()) return;
        if (SocialRegion.instance != null && SocialRegion.instance.isShowing) return;
        if (textPreview == null) return;

        if (lineRenderer == null)
        {
            lineRenderer = GetComponent<LineRenderer>();
            if (lineRenderer == null) return;
            lineRenderer.sortingLayerName = "MyLineRenderer";
            SetupLineRenderers();
        }

        if (Input.GetMouseButtonDown(0))
        {
            textPreview.SetText("");
            textPreview.FadeIn();
            ClearHighlightsSafe();
            starSeed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
            starLayoutDirty = true;
        }

        if (Input.GetMouseButton(0))
        {
            isDragging = true;
            textPreview.SetActive(true);

            mousePoint = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            mousePoint.z = 90;

            HashSet<char> allowedNext = null;
            if (WordRegion.instance != null && currentIndexes.Count > 0)
            {
                WordRegion.instance.GetAllowedNextLetters(textPreview.GetText(), allowedNextLetters);
                if (allowedNextLetters.Count > 0)
                {
                    allowedNext = allowedNextLetters;
                }
            }

            int nearest = GetNearestPosition(mousePoint, letterPositions, allowedNext);
            if (nearest != -1)
            {
                Vector3 letterPosition = letterPositions[nearest];
                if (currentIndexes.Count >= 2 && currentIndexes[currentIndexes.Count - 2] == nearest)
                {
                    currentIndexes.RemoveAt(currentIndexes.Count - 1);
                    textPreview.SetIndexes(currentIndexes);
                }
                else if (!currentIndexes.Contains(nearest))
                {
                    currentIndexes.Add(nearest);
                    textPreview.SetIndexes(currentIndexes);
                }
            }

            BuildPoints();
            UpdateHighlights();
        }
        else if (Input.GetMouseButtonUp(0))
        {
            isDragging = false;
            currentIndexes.Clear();
            lineRenderer.positionCount = 0;
            if (shimmerLineRenderer != null)
            {
                shimmerLineRenderer.positionCount = 0;
            }
            HideStars();
            ClearHighlightsSafe();

            WordRegion.instance.CheckAnswer(textPreview.GetText());
        }

        if (points.Count >= 2 && isDragging)
        {
            positions = iTween.GetSmoothPoints(points.ToArray(), 8);
            lineRenderer.positionCount = positions.Count;
            lineRenderer.SetPositions(positions.ToArray());
            if (shimmerLineRenderer != null)
            {
                shimmerLineRenderer.positionCount = positions.Count;
                shimmerLineRenderer.SetPositions(positions.ToArray());
            }
            UpdateGoldLine();
            UpdateShimmer();
            UpdateStars(positions);
        }
    }

    private int GetNearestPosition(Vector3 point, List<Vector3> letters, HashSet<char> allowedNext)
    {
        float min = float.MaxValue;
        int index = -1;
        for(int i = 0; i < letters.Count; i++)
        {
            float distant = Vector3.Distance(point, letters[i]);
            float radius = RADIUS;
            if (allowedNext != null && allowedNext.Count > 0)
            {
                char letter = GetLetterAtIndex(i);
                if (letter == '\0' || !allowedNext.Contains(letter))
                {
                    radius = RADIUS * nonMatchRadiusCoef;
                }
            }

            if (distant <= radius && distant < min)
            {
                min = distant;
                index = i;
            }
        }
        return index;
    }

    private char GetLetterAtIndex(int index)
    {
        if (string.IsNullOrEmpty(textPreview.word)) return '\0';
        if (index < 0 || index >= textPreview.word.Length) return '\0';
        return char.ToUpperInvariant(textPreview.word[index]);
    }

    private void SetupLineRenderers()
    {
        if (lineRenderer != null)
        {
            lineRenderer.useWorldSpace = true;
            lineRenderer.textureMode = LineTextureMode.Tile;
            lineRenderer.alignment = LineAlignment.View;
            if (goldLineMaterial == null && goldLineTexture != null)
            {
                Shader shader = lineShader != null ? lineShader : Shader.Find("Unlit/Transparent");
                if (shader != null)
                {
                    goldLineMaterial = new Material(shader);
                    goldLineMaterial.mainTexture = goldLineTexture;
                }
            }
            if (goldLineMaterial != null)
            {
                lineRenderer.material = goldLineMaterial;
            }
        }

        EnsureShimmerLine();
        if (shimmerLineRenderer != null)
        {
            shimmerLineRenderer.useWorldSpace = true;
            shimmerLineRenderer.textureMode = LineTextureMode.Tile;
            shimmerLineRenderer.alignment = lineRenderer != null ? lineRenderer.alignment : LineAlignment.View;
            shimmerLineRenderer.widthMultiplier = (lineRenderer != null ? lineRenderer.widthMultiplier : 0.1f) * shimmerWidthCoef;
            shimmerLineRenderer.sortingLayerName = lineRenderer != null ? lineRenderer.sortingLayerName : "Default";
            shimmerLineRenderer.sortingOrder = (lineRenderer != null ? lineRenderer.sortingOrder : 0) + 1;
            if (shimmerLineMaterial == null && shimmerTexture != null)
            {
                Shader shader = additiveShader != null ? additiveShader : Shader.Find("Particles/Additive");
                if (shader != null)
                {
                    shimmerLineMaterial = new Material(shader);
                    shimmerLineMaterial.mainTexture = shimmerTexture;
                }
            }
            if (shimmerLineMaterial != null)
            {
                shimmerLineRenderer.material = shimmerLineMaterial;
                shimmerLineRenderer.material.mainTextureScale = new Vector2(shimmerTiling, 1f);
            }
        }
    }

    private void EnsureShimmerLine()
    {
        if (shimmerLineRenderer != null) return;
        if (lineRenderer == null) return;

        GameObject go = new GameObject("LineShimmer");
        go.transform.SetParent(transform, false);
        shimmerLineRenderer = go.AddComponent<LineRenderer>();
        shimmerLineRenderer.widthCurve = lineRenderer.widthCurve;
        shimmerLineRenderer.numCapVertices = lineRenderer.numCapVertices;
        shimmerLineRenderer.numCornerVertices = lineRenderer.numCornerVertices;
        shimmerLineRenderer.shadowCastingMode = lineRenderer.shadowCastingMode;
        shimmerLineRenderer.receiveShadows = lineRenderer.receiveShadows;
    }

    private void BuildPoints()
    {
        points.Clear();
        foreach (var i in currentIndexes) points.Add(letterPositions[i]);

        if (currentIndexes.Count == 1 || points.Count >= 1 && Vector3.Distance(mousePoint, points[points.Count - 1]) >= RADIUS)
        {
            points.Add(mousePoint);
        }
    }

    private void UpdateHighlights()
    {
        if (WordRegion.instance == null)
        {
            return;
        }

        if (string.IsNullOrEmpty(textPreview.word))
        {
            WordRegion.instance.ClearHighlights();
            return;
        }

        if (currentIndexes.Count == 0)
        {
            WordRegion.instance.ClearHighlights();
            return;
        }

        int lastIndex = currentIndexes[currentIndexes.Count - 1];
        if (lastIndex >= 0 && lastIndex < textPreview.word.Length)
        {
            WordRegion.instance.SetHighlightLetter(textPreview.word[lastIndex]);
        }
        else
        {
            WordRegion.instance.ClearHighlights();
        }
    }

    private void ClearHighlightsSafe()
    {
        if (WordRegion.instance != null)
        {
            WordRegion.instance.ClearHighlights();
        }
    }

    private void UpdateShimmer()
    {
        if (shimmerLineRenderer == null || shimmerLineRenderer.material == null) return;
        shimmerOffset += shimmerScrollSpeed * Time.deltaTime;
        shimmerLineRenderer.material.mainTextureOffset = new Vector2(shimmerOffset, 0f);
    }

    private void UpdateGoldLine()
    {
        if (lineRenderer == null || lineRenderer.material == null) return;
        if (goldScrollSpeed == 0f) return;
        goldOffset += goldScrollSpeed * Time.deltaTime;
        lineRenderer.material.mainTextureOffset = new Vector2(goldOffset, 0f);
    }

    private void UpdateStars(List<Vector3> pts)
    {
        if (starSprite == null || pts == null || pts.Count < 2)
        {
            HideStars();
            return;
        }

        float totalLength = GetPolylineLength(pts);
        if (totalLength <= 0f)
        {
            HideStars();
            return;
        }

        if (starSpacing <= 0.01f) starSpacing = 0.01f;
        int count = Mathf.FloorToInt(totalLength / starSpacing) + 1;
        if (count < 2) count = 2;
        EnsureStarPool(count);

        if (starLayoutDirty || count != lastStarCount || Mathf.Abs(totalLength - lastStarLineLength) > starSpacing * 0.5f)
        {
            RebuildStarDistances(totalLength, count);
            starLayoutDirty = false;
            lastStarCount = count;
            lastStarLineLength = totalLength;
        }

        for (int i = 0; i < count; i++)
        {
            float distance = Mathf.Min(starDistances[i], totalLength);
            Vector3 pos = GetPointAlongPolyline(pts, distance);

            SpriteRenderer sr = starRenderers[i];
            if (sr == null) continue;
            sr.gameObject.SetActive(true);
            sr.transform.position = pos;
            sr.transform.localScale = Vector3.one * starSize;

            float flicker = Mathf.PerlinNoise(starPhases[i], Time.time * starFlickerSpeed);
            float alpha = Mathf.Lerp(starAlphaMin, starAlphaMax, flicker);
            sr.color = new Color(starColor.r, starColor.g, starColor.b, alpha);
        }

        for (int i = count; i < starRenderers.Count; i++)
        {
            if (starRenderers[i] != null) starRenderers[i].gameObject.SetActive(false);
        }
    }

    private void EnsureStarPool(int count)
    {
        if (starRoot == null)
        {
            GameObject root = new GameObject("LineStars");
            root.transform.SetParent(transform, false);
            starRoot = root.transform;
        }

        if (starMaterial == null)
        {
            Shader shader = additiveShader != null ? additiveShader : Shader.Find("Particles/Additive");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }
            if (shader != null)
            {
                starMaterial = new Material(shader);
            }
        }

        while (starRenderers.Count < count)
        {
            GameObject go = new GameObject($"Star_{starRenderers.Count}");
            go.transform.SetParent(starRoot, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = starSprite;
            if (starMaterial != null) sr.material = starMaterial;
            sr.sortingLayerName = lineRenderer != null ? lineRenderer.sortingLayerName : "Default";
            sr.sortingOrder = (lineRenderer != null ? lineRenderer.sortingOrder : 0) + 2;
            starRenderers.Add(sr);
            starPhases.Add(UnityEngine.Random.Range(0f, 100f));
            starDistances.Add(0f);
        }
    }

    private void HideStars()
    {
        for (int i = 0; i < starRenderers.Count; i++)
        {
            if (starRenderers[i] != null) starRenderers[i].gameObject.SetActive(false);
        }
    }

    private float GetPolylineLength(List<Vector3> pts)
    {
        float length = 0f;
        for (int i = 1; i < pts.Count; i++)
        {
            length += Vector3.Distance(pts[i - 1], pts[i]);
        }
        return length;
    }

    private float GetStarStep()
    {
        float baseStep = Mathf.Max(0.01f, starSpacing);
        float jitter = Mathf.Clamp01(starSpacingJitter);
        if (jitter <= 0f) return baseStep;

        float minStep = baseStep * (1f - jitter);
        float maxStep = baseStep * (1f + jitter);
        return UnityEngine.Random.Range(minStep, maxStep);
    }

    private void RebuildStarDistances(float totalLength, int count)
    {
        if (count <= 0) return;
        if (starDistances.Count < count)
        {
            for (int i = starDistances.Count; i < count; i++)
            {
                starDistances.Add(0f);
            }
        }

        if (count == 1)
        {
            starDistances[0] = 0f;
            return;
        }

        float baseStep = Mathf.Max(0.01f, starSpacing);
        float jitter = Mathf.Clamp01(starSpacingJitter);
        float minStep = baseStep * (1f - jitter);
        float maxStep = baseStep * (1f + jitter);

        System.Random rng = new System.Random(starSeed);
        starDistances[0] = 0f;
        starDistances[count - 1] = totalLength;

        float distance = 0f;
        for (int i = 1; i < count - 1; i++)
        {
            float t = (float)rng.NextDouble();
            float step = jitter <= 0f ? baseStep : Mathf.Lerp(minStep, maxStep, t);
            distance += step;
            float remaining = Mathf.Max(0f, totalLength - distance);
            int remainingStars = (count - 1) - i;
            if (remainingStars > 0)
            {
                float minRemaining = remainingStars * minStep;
                float maxAllowed = totalLength - minRemaining;
                if (distance > maxAllowed) distance = maxAllowed;
            }

            starDistances[i] = Mathf.Clamp(distance, 0f, totalLength);
        }
    }

    private Vector3 GetPointAlongPolyline(List<Vector3> pts, float distance)
    {
        if (pts.Count == 0) return Vector3.zero;
        if (distance <= 0f) return pts[0];

        float remaining = distance;
        for (int i = 1; i < pts.Count; i++)
        {
            float segment = Vector3.Distance(pts[i - 1], pts[i]);
            if (remaining <= segment)
            {
                float t = segment <= 0.0001f ? 0f : remaining / segment;
                return Vector3.Lerp(pts[i - 1], pts[i], t);
            }
            remaining -= segment;
        }
        return pts[pts.Count - 1];
    }
}
