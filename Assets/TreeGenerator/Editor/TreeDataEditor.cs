using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TreeData))]
public class TreeDataEditor : Editor
{
    // Foldout states (static so they persist between selection changes)
    static bool _seedFold    = true;
    static bool _trunkFold   = true;
    static bool _branchFold  = true;
    static bool _subFold     = false;
    static bool _sub2Fold    = false;
    static bool _leafFold    = false;

    // Cached section header style
    static GUIStyle _headerStyle;

    static GUIStyle HeaderStyle
    {
        get
        {
            if (_headerStyle == null)
            {
                _headerStyle = new GUIStyle(EditorStyles.foldoutHeader)
                {
                    fontStyle = FontStyle.Bold,
                    fontSize  = 12
                };
            }
            return _headerStyle;
        }
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // ── Seed ──────────────────────────────────────────────────────────────
        _seedFold = BeginSection("Losowość", _seedFold, new Color(0.55f, 0.55f, 0.75f));
        if (_seedFold)
        {
            Prop("seed", "Ziarno losowości",
                "Zmień tę liczbę, aby uzyskać inny kształt drzewa przy tych samych parametrach.");
        }
        EndSection();

        // ── Trunk ─────────────────────────────────────────────────────────────
        _trunkFold = BeginSection("Pień", _trunkFold, new Color(0.60f, 0.45f, 0.25f));
        if (_trunkFold)
        {
            var t = serializedObject.FindProperty("trunk");
            DrawProp(t, "height",        "Wysokość",       "Całkowita wysokość pnia.");
            DrawProp(t, "segments",      "Segmenty",       "Liczba pierścieni wzdłuż pnia (jakość krzywizny).");
            DrawProp(t, "sides",         "Boki",           "Ilość wierzchołków na każdym pierścieniu (gładkość).");
            DrawProp(t, "maxRadius",     "Max promień",    "Maksymalny promień pnia (górny limit dla krzywej promienia).");
            DrawProp(t, "reduceSidesPerSegmentToThree", "Zmniejszaj boki co segment", "Jeśli włączone, liczba boków pnia maleje co segment i na końcu osiąga 3.");
            EditorGUILayout.Space(4);
            DrawProp(t, "radiusCurve",   "Krzywa promienia","Promień pnia od podstawy (X=0) do wierzchołka (X=1).");
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Kształt / wygięcie", EditorStyles.miniLabel);
            DrawProp(t, "bendAngle",     "Kąt wygięcia",   "O ile stopni pień odchyla się od pionu.");
            DrawProp(t, "bendDirection", "Kierunek wygięcia","Strona świata, ku której pień się wygina (0°=+Z, 90°=+X).");
            DrawProp(t, "twist",         "Skręcenie",      "Steruje zmianą kierunku wygięcia pnia wzdłuż jego wysokości (stopnie łącznie).");
        }
        EndSection();

        // ── Main branches ─────────────────────────────────────────────────────
        _branchFold = BeginSection("Główne gałęzie", _branchFold, new Color(0.30f, 0.55f, 0.30f));
        if (_branchFold)
        {
            var b = serializedObject.FindProperty("mainBranches");
            DrawProp(b, "enabled", "Włączone");
            if (serializedObject.FindProperty("mainBranches").FindPropertyRelative("enabled").boolValue)
            {
                EditorGUILayout.LabelField("Dystrybucja", EditorStyles.miniLabel);
                DrawProp(b, "count",        "Liczba gałęzi",    "Całkowita liczba głównych gałęzi.");
                DrawProp(b, "startHeight",  "Wysokość start",   "Punkt na pniu (0=dół, 1=góra) od którego zaczynają się gałęzie.");
                DrawProp(b, "endHeight",    "Wysokość koniec",  "Punkt na pniu do którego sięgają gałęzie.");
                DrawProp(b, "densityCurve", "Krzywa gęstości",  "Prawdopodobieństwo gałęzi na danej wysokości (X=dół→góra zakresu).");
                DrawProp(b, "enforceMinSeparation", "Wymuś minimalny odstęp", "Ogranicza losowanie gałęzi zbyt blisko siebie.");
                DrawProp(b, "minHeightSeparation", "Min odstęp wysokości", "Minimalny odstęp po wysokości pnia (0..1).");
                DrawProp(b, "minAngularSeparation", "Min odstęp kątowy", "Minimalny odstęp wokół pnia (stopnie).");
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Długość", EditorStyles.miniLabel);
                DrawProp(b, "minLength",     "Min długość");
                DrawProp(b, "maxLength",     "Max długość");
                DrawProp(b, "lengthByHeight","Długość wg. wysokości","Mnożnik długości w zależności od położenia na pniu.");
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Kąt i wygięcie", EditorStyles.miniLabel);
                DrawProp(b, "minAngle",    "Min kąt",         "Minimalny kąt od osi pnia (stopnie).");
                DrawProp(b, "maxAngle",    "Max kąt",         "Maksymalny kąt od osi pnia (stopnie).");
                DrawProp(b, "bendAmount",  "Wygięcie",        "Stopień opadania gałęzi (grawitacja) na całej jej długości.");
                DrawProp(b, "twistAmount", "Skręcenie",       "Steruje zmianą kierunku wygięcia wzdłuż długości gałęzi (stopnie łącznie).");
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Geometria", EditorStyles.miniLabel);
                DrawProp(b, "segments",    "Max segmentów", "Maksymalna liczba segmentów dla najdłuższej gałęzi.");
                DrawProp(b, "sides",       "Boki");
                DrawProp(b, "segmentsByBranchLength", "Segmenty vs długość gałęzi", "X: najkrótsza→najdłuższa główna gałąź w drzewie. Y: udział max segmentów (0..1).");
                DrawProp(b, "reduceSidesPerSegmentToThree", "Zmniejszaj boki co segment", "Jeśli włączone, boki gałęzi maleją co segment i na końcu osiągają 3.");
                DrawProp(b, "maxRadius", "Max promień", "Maksymalny promień gałęzi. Wartość 1 na krzywej promienia oznacza dokładnie ten promień.");
                DrawProp(b, "maxRadiusByStartHeight", "Max promień wg wysokości startu", "Zmienia maksymalny promień zależnie od wysokości początku gałęzi na pniu.");
                DrawProp(b, "radiusCurve", "Krzywa promienia (0..1)", "Znormalizowana grubość gałęzi od nasady (X=0) do końca (X=1). 1 = max promień.");
            }
        }
        EndSection();

        // ── Sub-branches ──────────────────────────────────────────────────────
        _subFold = BeginSection("Gałązki", _subFold, new Color(0.30f, 0.65f, 0.40f));
        if (_subFold)
        {
            var s = serializedObject.FindProperty("subBranches");
            DrawProp(s, "enabled", "Włączone");
            if (serializedObject.FindProperty("subBranches").FindPropertyRelative("enabled").boolValue)
            {
                DrawProp(s, "countPerBranch", "Na gałąź",         "Liczba gałązek na każdej głównej gałęzi.");
                DrawProp(s, "countByTrunkHeight", "Liczba wg wysokości", "Mnożnik max liczby gałązek zależny od wysokości startu gałęzi głównej na pniu.");
                DrawProp(s, "countByParentLength", "Liczba wg długości gałęzi", "Mnożnik max liczby gałązek zależny od długości gałęzi głównej (X: 0 najkrótsza, 1 najdłuższa).");
                DrawProp(s, "startPosition",  "Pozycja start",    "Punkt wzdłuż gałęzi od którego zaczynają się gałązki (0=nasada, 1=koniec).");
                DrawProp(s, "endPosition",    "Pozycja koniec",   "Punkt wzdłuż gałęzi do którego sięgają gałązki.");
                DrawProp(s, "lengthRatio",    "Stosunek długości", "Długość gałązki jako ułamek długości gałęzi-rodzica.");
                EditorGUILayout.Space(4);
                DrawProp(s, "minAngle",    "Min kąt");
                DrawProp(s, "maxAngle",    "Max kąt");
                DrawProp(s, "bendAmount",  "Wygięcie");
                EditorGUILayout.Space(4);
                DrawProp(s, "segments",    "Max segmentów", "Maksymalna liczba segmentów dla najdłuższej gałązki.");
                DrawProp(s, "sides",       "Boki");
                DrawProp(s, "radiusCurve", "Krzywa promienia");
            }
        }
        EndSection();

        // ── Sub-branches level 2 ─────────────────────────────────────────────
        _sub2Fold = BeginSection("Gałązki 2 poziom", _sub2Fold, new Color(0.25f, 0.58f, 0.40f));
        if (_sub2Fold)
        {
            var s2 = serializedObject.FindProperty("subBranchesLevel2");
            DrawProp(s2, "enabled", "Włączone");
            if (serializedObject.FindProperty("subBranchesLevel2").FindPropertyRelative("enabled").boolValue)
            {
                DrawProp(s2, "countPerBranch", "Na gałązkę", "Liczba gałązek 2 poziomu na każdą gałązkę 1 poziomu.");
                DrawProp(s2, "countByParentLength", "Liczba wg długości rodzica", "Mnożnik liczby wg długości gałązki 1 poziomu.");
                DrawProp(s2, "startPosition", "Pozycja start");
                DrawProp(s2, "endPosition", "Pozycja koniec");
                DrawProp(s2, "lengthRatio", "Stosunek długości");
                EditorGUILayout.Space(4);
                DrawProp(s2, "minAngle", "Min kąt");
                DrawProp(s2, "maxAngle", "Max kąt");
                DrawProp(s2, "bendAmount", "Wygięcie");
                EditorGUILayout.Space(4);
                DrawProp(s2, "segments", "Max segmentów");
                DrawProp(s2, "sides", "Boki");
                DrawProp(s2, "radiusCurve", "Krzywa promienia");
            }
        }
        EndSection();

        // ── Leaves ────────────────────────────────────────────────────────────
        _leafFold = BeginSection("Liście", _leafFold, new Color(0.25f, 0.72f, 0.25f));
        if (_leafFold)
        {
            var l = serializedObject.FindProperty("leaves");
            DrawProp(l, "enabled", "Włączone");
            if (serializedObject.FindProperty("leaves").FindPropertyRelative("enabled").boolValue)
            {
                EditorGUILayout.LabelField("Dystrybucja", EditorStyles.miniLabel);
                DrawProp(l, "countPerTipMainBranch", "Na końcówkę gałęzi", "Liczba liści na końcówce każdej gałęzi głównej.");
                DrawProp(l, "countPerTipSubBranch",  "Na końcówkę gałązki", "Liczba liści na końcówce każdej gałązki.");
                DrawProp(l, "countPerTipSubBranchLevel2",  "Na końcówkę gałązki 2", "Liczba liści na końcówce każdej gałązki 2 poziomu.");
                DrawProp(l, "alongBranchStart", "Start wzdłuż gałęzi", "Od tej pozycji (0=nasada, 1=koniec) liście pojawiają się też wzdłuż gałęzi.");
                DrawProp(l, "countAlongMainBranch", "Wzdłuż gałęzi", "Dodatkowe liście rozłożone ciągle wzdłuż gałęzi głównej (niezależnie od węzłów i końcówek).");
                DrawProp(l, "countAlongSubBranch",  "Wzdłuż gałązki", "Dodatkowe liście rozłożone ciągle wzdłuż gałązki (niezależnie od węzłów i końcówek).");
                DrawProp(l, "countAlongSubBranchLevel2",  "Wzdłuż gałązki 2", "Dodatkowe liście rozłożone ciągle wzdłuż gałązki 2 poziomu.");
                DrawProp(l, "alongDistributionMainBranch", "Dystrybucja wzdłuż gałęzi", "Krzywa gęstości dodatkowych liści wzdłuż gałęzi głównej.");
                DrawProp(l, "alongDistributionSubBranch",  "Dystrybucja wzdłuż gałązki", "Krzywa gęstości dodatkowych liści wzdłuż gałązki.");
                DrawProp(l, "alongDistributionSubBranchLevel2",  "Dystrybucja wzdłuż gałązki 2", "Krzywa gęstości dodatkowych liści wzdłuż gałązki 2 poziomu.");
                DrawProp(l, "enforceMinSeparation", "Wymuś min odstępy", "Zapobiega generowaniu liści zbyt blisko siebie.");
                DrawProp(l, "minSeparationMainBranch", "Min odstęp gałęzi", "Minimalna odległość 3D między liśćmi na gałęziach głównych.");
                DrawProp(l, "minSeparationSubBranch",  "Min odstęp gałązek", "Minimalna odległość 3D między liśćmi na gałązkach.");
                DrawProp(l, "minSeparationSubBranchLevel2",  "Min odstęp gałązek 2", "Minimalna odległość 3D między liśćmi na gałązkach 2 poziomu.");
                DrawProp(l, "placementAttemptsPerLeaf", "Próby rozmieszczenia", "Liczba prób ustawienia liścia z zachowaniem minimalnego odstępu.");
                DrawProp(l, "countPerNodeMainBranch", "Na węzeł gałęzi", "Liczba liści na każdym węźle gałęzi głównej poza końcówką.");
                DrawProp(l, "countPerNodeSubBranch",  "Na węzeł gałązki", "Liczba liści na każdym węźle gałązki poza końcówką.");
                DrawProp(l, "countPerNodeSubBranchLevel2",  "Na węzeł gałązki 2", "Liczba liści na każdym węźle gałązki 2 poziomu poza końcówką.");
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Rozmiar i kształt", EditorStyles.miniLabel);
                DrawProp(l, "minSize",     "Min rozmiar");
                DrawProp(l, "maxSize",     "Max rozmiar");
                DrawProp(l, "sizeByTreeHeight", "Rozmiar wg wysokości drzewa", "Mnożnik rozmiaru liścia zależny od wysokości na pniu.");
                DrawProp(l, "sizeByDistanceFromTrunk", "Rozmiar wg odległości od pnia", "Mnożnik rozmiaru liścia zależny od odległości od obwodu pnia.");
                DrawProp(l, "spreadAngle", "Kąt rozkładu",  "Stożek losowej orientacji liści wokół osi gałęzi.");
                DrawProp(l, "outwardDirectionCorrelation", "Korelacja z obwodem pnia", "W jakim stopniu kierunek płatka liścia ma korelować z kierunkiem na zewnątrz obwodu pnia.");
                DrawProp(l, "droop",       "Opadanie",      "Jak bardzo liście opadają w dół (0=brak, 1=maksymalnie).");
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Kontrola kierunku", EditorStyles.miniLabel);
                DrawProp(l, "validateUpwardOrientation", "Sprawdzaj kierunek ku górze", "Sprawdza, czy liście są generalnie skierowane ku górze.");
                DrawProp(l, "upwardDotThreshold", "Próg dot ku górze", "Liść uznawany za skierowany ku górze, gdy dot(kierunek, worldUp) przekracza ten próg.");
                DrawProp(l, "minUpwardLeafRatio", "Min udział liści ku górze", "Minimalny udział liści skierowanych ku górze wymagany do zaliczenia testu.");
                EditorGUILayout.Space(4);
                DrawProp(l, "color", "Kolor wierzchołka",   "Kolor wierzchołkowy liści – widoczny z shaderem vertex-color.");
            }
        }
        EndSection();

        serializedObject.ApplyModifiedProperties();
    }

    // ── Section helpers ───────────────────────────────────────────────────────

    private bool BeginSection(string label, bool foldout, Color accent)
    {
        EditorGUILayout.Space(3);

        // Colored bar behind the foldout header
        Rect rect = EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUI.color = accent * 0.85f + Color.white * 0.15f;
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 22), Texture2D.whiteTexture);
        GUI.color = Color.white;

        bool result = EditorGUILayout.BeginFoldoutHeaderGroup(foldout, label, HeaderStyle);
        EditorGUILayout.EndFoldoutHeaderGroup();

        if (result) EditorGUILayout.Space(4);
        return result;
    }

    private void EndSection()
    {
        EditorGUILayout.Space(3);
        EditorGUILayout.EndVertical();
    }

    // Draw a relative property with an optional custom label and tooltip
    private void DrawProp(SerializedProperty parent, string name,
                          string label = null, string tooltip = null)
    {
        SerializedProperty p = parent.FindPropertyRelative(name);
        if (p == null)
        {
            EditorGUILayout.HelpBox($"Property '{name}' not found.", MessageType.Warning);
            return;
        }

        GUIContent content = label != null
            ? new GUIContent(label, tooltip ?? string.Empty)
            : new GUIContent(p.displayName, tooltip ?? p.tooltip);

        EditorGUILayout.PropertyField(p, content, true);
    }

    // Shortcut for top-level properties
    private void Prop(string name, string label = null, string tooltip = null)
    {
        SerializedProperty p = serializedObject.FindProperty(name);
        if (p == null) return;
        GUIContent content = label != null
            ? new GUIContent(label, tooltip ?? string.Empty)
            : new GUIContent(p.displayName, p.tooltip);
        EditorGUILayout.PropertyField(p, content, true);
    }
}
