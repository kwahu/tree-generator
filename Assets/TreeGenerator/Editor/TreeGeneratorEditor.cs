using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TreeGenerator))]
public class TreeGeneratorEditor : Editor
{
    private static int _lodInspectorTab;

    private static readonly string[] LodTabNames =
    {
        "Ogólne",
        "Billboardy",
        "Bryła korony",
        "Liście / grupy",
        "Lightmapy"
    };

    public override void OnInspectorGUI()
    {
        var gen = (TreeGenerator)target;

        EditorGUI.BeginChangeCheck();
        serializedObject.Update();

        // ── Stats bar ─────────────────────────────────────────────────────────
        if (gen.lastVertexCount > 0)
        {
            GUI.color = new Color(0.85f, 0.95f, 1f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.color = Color.white;
            EditorGUILayout.LabelField(
                $"Wierzchołki: {gen.lastVertexCount:N0}   " +
                $"Trójkąty: {gen.lastTriangleCount:N0}",
                EditorStyles.miniLabel);
            EditorGUILayout.LabelField(
                $"Liście: {gen.lastLeafCount:N0}   Zgrupowane liście: {gen.lastClusterLeafCount:N0}",
                EditorStyles.miniLabel);
            if (gen.lastLodWoodTriangleCounts != null && gen.lastLodWoodTriangleCounts.Count > 0)
            {
                for (int i = 0; i < gen.lastLodWoodTriangleCounts.Count; i++)
                {
                    int wood = gen.lastLodWoodTriangleCounts[i];
                    int leaf = (gen.lastLodLeafTriangleCounts != null && i < gen.lastLodLeafTriangleCounts.Count)
                        ? gen.lastLodLeafTriangleCounts[i]
                        : 0;
                    int clustered = (gen.lastLodClusterLeafCounts != null && i < gen.lastLodClusterLeafCounts.Count)
                        ? gen.lastLodClusterLeafCounts[i]
                        : 0;
                    EditorGUILayout.LabelField(
                        $"LOD{i}  Drzewo: {wood:N0} tri   Liście: {leaf:N0} tri   Zgrupowane: {clustered:N0}",
                        EditorStyles.miniLabel);
                }
            }
            EditorGUILayout.LabelField(
                $"Liście ku górze: {gen.lastUpwardLeafRatio:P1}   Test: {(gen.lastUpwardLeafCheckPassed ? "OK" : "NIE")}",
                EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(4);
        }

        // ── Data field ────────────────────────────────────────────────────────
        EditorGUILayout.PropertyField(serializedObject.FindProperty("data"),
            new GUIContent("Tree Data"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("treeMaterial"),
            new GUIContent("Materiał drzewa", "Materiał dla pnia i gałęzi (submesh 0)."));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("leafMaterial"),
            new GUIContent("Materiał liści", "Materiał dla liści (submesh 1)."));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("clusterLeafMaterial"),
            new GUIContent("Materiał grup liści", "Materiał dla zgrupowanych liści LOD (submesh 2)."));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("forceLeafNormalsToSun"),
            new GUIContent("Liście: odwracaj normalne od słońca", "Tylko liście z normalnymi przeciwnymi do słońca są odwracane; pozostałe zachowują naturalny shading."));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("leafBakeMinSunDot"),
            new GUIContent("Liście: min dot do bake", "Minimalny dot(normal, kierunek słońca) dla liści podczas bake; podnosi tylko zbyt ciemne przypadki."));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("woodUvWorldSize"),
            new GUIContent("UV drewna: rozmiar świata", "Ile jednostek świata przypada na 1 tile UV drewna (stały texel na długości i obwodzie)."));
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Wiatr liści (vertex shader)", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Materiały: shadery „TreeGenerator/Leaf Baked Lit…” i „…Leaf Billboard Opaque”. Parametry idą w MaterialPropertyBlock na wszystkie MeshRenderery pod drzewem (LOD, billboardy).",
            MessageType.None);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("leafWindEnabled"),
            new GUIContent("Włącz wiatr"));
        if (serializedObject.FindProperty("leafWindEnabled").boolValue)
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("leafWindStrength"),
                new GUIContent("Siła przemieszczenia", "Amplituda w jednostkach świata (przy czubku liścia)."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("leafWindFrequency"),
                new GUIContent("Częstotliwość", "Szybkość oscylacji (skalowana z _Time)."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("leafWindTurbulence"),
                new GUIContent("Turbulencja", "Dodatkowe drugie drganie / szum przestrzenny."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("leafWindPhaseScale"),
                new GUIContent("Skala fazy", "Różnica fazy między liśćmi w przestrzeni (więcej = mniej synchronicznie)."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("leafWindMaskExponent"),
                new GUIContent("Maska czubka (wykładnik)", "Wykładnik uv.y: wyżej = więcej ruchu przy czubku, mniej przy nasadzie."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("leafWindDirection"),
                new GUIContent("Kierunek wiatru (świat)"));
        }
        EditorGUILayout.Space(4);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("autoUpdateWhenValid"),
            new GUIContent("Auto aktualizacja",
                "Automatycznie przebudowuje drzewo podczas edycji parametrów, tylko jeśli są poprawne."));
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("LOD", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("autoGenerateLods"),
            new GUIContent("Auto LOD", "Automatycznie tworzy i konfiguruje LODGroup oraz poziomy LOD."));
        if (serializedObject.FindProperty("autoGenerateLods").boolValue)
        {
            _lodInspectorTab = GUILayout.Toolbar(_lodInspectorTab, LodTabNames);
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            switch (_lodInspectorTab)
            {
                case 0:
                    DrawLodTabGeneral(serializedObject);
                    break;
                case 1:
                    DrawLodTabBillboards(serializedObject);
                    break;
                case 2:
                    DrawLodTabVolume(serializedObject);
                    break;
                case 3:
                    DrawLodTabLeavesAndClustering(serializedObject);
                    break;
                case 4:
                    DrawLodTabLightmap(serializedObject);
                    break;
            }
            EditorGUILayout.EndVertical();
        }
        bool generatorSettingsChanged = EditorGUI.EndChangeCheck();
        serializedObject.ApplyModifiedProperties();

        bool hasValidData = gen.TryValidateSettings(out string validationMessage);

        if (!hasValidData)
            EditorGUILayout.HelpBox(validationMessage, MessageType.Warning);

        EditorGUILayout.Space(8);

        // ── Generate button ───────────────────────────────────────────────────
        GUI.backgroundColor = new Color(0.45f, 0.88f, 0.45f);
        if (GUILayout.Button("Generuj Drzewo", GUILayout.Height(38)))
        {
            Undo.RecordObject(gen.GetComponent<MeshFilter>(), "Generate Tree");
            gen.Generate();
            EditorUtility.SetDirty(gen);
        }
        GUI.backgroundColor = Color.white;
        if (GUILayout.Button("Generuj Lightmap UV (UV2)", GUILayout.Height(24)))
        {
            gen.GenerateLightmapUVs();
            EditorUtility.SetDirty(gen);
        }

        // ── Inline TreeData editor ────────────────────────────────────────────
        if (gen.data != null)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Parametry drzewa", EditorStyles.boldLabel);
            Editor dataEditor = CreateEditor(gen.data);

            EditorGUI.BeginChangeCheck();
            dataEditor.OnInspectorGUI();
            bool dataChanged = EditorGUI.EndChangeCheck();

            if ((dataChanged || generatorSettingsChanged) && gen.autoUpdateWhenValid && gen.TryValidateSettings(out _))
            {
                Undo.RecordObject(gen.GetComponent<MeshFilter>(), "Auto Generate Tree");
                gen.Generate();
                EditorUtility.SetDirty(gen);
                EditorUtility.SetDirty(gen.data);
            }
        }
        else if (generatorSettingsChanged && gen.autoUpdateWhenValid && gen.TryValidateSettings(out _))
        {
            Undo.RecordObject(gen.GetComponent<MeshFilter>(), "Auto Generate Tree");
            gen.Generate();
            EditorUtility.SetDirty(gen);
        }
    }

    private static void DrawLodTabGeneral(SerializedObject so)
    {
        EditorGUILayout.LabelField("Przejścia i redukcja liści (klasyczne LOD)", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(so.FindProperty("lodLevels"),
            new GUIContent("Poziomy LOD", "Liczba poziomów LOD (wliczając LOD0)."));
        EditorGUILayout.PropertyField(so.FindProperty("lodStartScreenRelativeHeight"),
            new GUIContent("Start ekranu LOD0", "Screen Relative Transition Height dla LOD0."));
        EditorGUILayout.PropertyField(so.FindProperty("lodEndScreenRelativeHeight"),
            new GUIContent("Koniec ekranu ostatniego LOD", "Screen Relative Transition Height dla najdalszego LOD."));
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Ukrywanie siatki drewna gałęzi na LOD", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "LOD0 = główny mesh. Dalsze poziomy to dzieci __AutoLOD_1, __AutoLOD_2, … Próg = od którego indeksu włącznie dana kategoria nie dostaje geometrii rury (krzywa gałęzi nadal jest liczona pod liście). Wartość 6 = zawsze rysuj.",
            MessageType.None);
        EditorGUILayout.PropertyField(so.FindProperty("lodHideMainBranchWoodFromLevel"),
            new GUIContent("Ukryj główne gałęzie od LOD", "Domyślnie 3: na LOD3+ brak rur głównych gałęzi."));
        EditorGUILayout.PropertyField(so.FindProperty("lodHideSubBranchWoodFromLevel"),
            new GUIContent("Ukryj podgałęzie od LOD", "Domyślnie 2."));
        EditorGUILayout.PropertyField(so.FindProperty("lodHideSubBranchLevel2WoodFromLevel"),
            new GUIContent("Ukryj gałązki (poz. 2) od LOD", "Domyślnie 1."));
        EditorGUILayout.Space(4);
        EditorGUILayout.PropertyField(so.FindProperty("lodFinalLeafCountMultiplier"),
            new GUIContent("Liście: końcowy mnożnik ilości", "Agresywna redukcja ilości liści na najdalszym LOD."));
        EditorGUILayout.PropertyField(so.FindProperty("lodFinalLeafSizeMultiplier"),
            new GUIContent("Liście: końcowy mnożnik rozmiaru", "Powiększenie liści na najdalszym LOD dla zachowania wizualnej masy."));
        EditorGUILayout.PropertyField(so.FindProperty("lodLeafReductionExponent"),
            new GUIContent("Liście: agresywność redukcji", "Wyższa wartość = szybsza redukcja liści na wcześniejszych LOD."));
        EditorGUILayout.PropertyField(so.FindProperty("lodLeafCountReductionStartLevel"),
            new GUIContent("Liście: redukcja ilości od LOD", "1 = od pierwszego zdalnego LOD; wyżej = wcześniejsze zdalne LOD bez mnożnika ilości (tip/node nadal wyłączone od LOD 1)."));
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Geometria drewna (LOD)", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(so.FindProperty("lodPreserveTrunkSegments"),
            new GUIContent("Pełne segmenty pnia na LOD", "Bez zmniejszania segmentów pnia na dalszych LOD (krzywizna pnia jak LOD0). Liczba boków rury pnia nadal maleje z LOD."));
        EditorGUILayout.PropertyField(so.FindProperty("lodPreserveBranchSegments"),
            new GUIContent("Pełne segmenty gałęzi na LOD", "Bez zmniejszania segmentów głównych gałęzi / podgałęzi / gałązek na dalszych LOD (kształt zgięć jak LOD0). Liczba boków rur nadal maleje z LOD."));
        EditorGUILayout.PropertyField(so.FindProperty("lodFlatVerticalBranchWood"),
            new GUIContent("Płaska wstęga gałęzi", "Wstęga w płaszczyźnie pionowej: grubość czytelna z boku, z góry/dolu cienka sylwetka. Pień zawsze pełny."));
        if (so.FindProperty("lodFlatVerticalBranchWood").boolValue)
        {
            EditorGUILayout.PropertyField(so.FindProperty("lodFlatVerticalBranchStartLevel"),
                new GUIContent("Od poziomu LOD", "1 = od pierwszego zdalnego LOD; wyżej = tylko na dalszych poziomach."));
        }
    }

    private static void DrawLodTabBillboards(SerializedObject so)
    {
        EditorGUILayout.LabelField("Zamiast trójkątów liści — quady billboard (camera-facing)", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(so.FindProperty("useBillboardLeafLods"),
            new GUIContent("Włącz billboardy korony", "Na wybranych LOD: nieprzezroczyste quady z shaderem camera-facing. Ma pierwszeństwo przed bryłą volume."));
        if (!so.FindProperty("useBillboardLeafLods").boolValue)
            return;

        EditorGUILayout.HelpBox(
            "Materiał: shader „TreeGenerator/Leaf Billboard Opaque” (lub własny billboard). Jeśli pole puste, użyty zostanie Materiał liści — może wyglądać źle bez odpowiedniego shadera.",
            MessageType.Info);
        EditorGUILayout.PropertyField(so.FindProperty("billboardLeafLodStartLevel"),
            new GUIContent("Billboard od LOD", "Od którego poziomu LOD (1+) stosować billboardy zamiast liści."));
        EditorGUILayout.PropertyField(so.FindProperty("billboardLeafLodMaxSprites"),
            new GUIContent("Max sprite’ów", "Losowy podzbiór środków trójkątów liści z LOD0."));
        EditorGUILayout.PropertyField(so.FindProperty("billboardLeafWorldWidth"),
            new GUIContent("Szerokość quadów"));
        EditorGUILayout.PropertyField(so.FindProperty("billboardLeafWorldHeight"),
            new GUIContent("Wysokość quadów"));
        EditorGUILayout.PropertyField(so.FindProperty("billboardLeafJitterRadius"),
            new GUIContent("Jitter pozycji", "Losowy offset próbek w świecie (mniej regularny „mur”)."));
        EditorGUILayout.PropertyField(so.FindProperty("billboardLeafMaterial"),
            new GUIContent("Materiał billboardów", "Zalecany: shader Leaf Billboard Opaque + tekstura liścia."));
    }

    private static void DrawLodTabVolume(SerializedObject so)
    {
        EditorGUILayout.LabelField("Bryła korony z marching + voxel field", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(so.FindProperty("useLeafVolumeLods"),
            new GUIContent("Włącz bryłę korony (volume)", "Dla dalszych LOD zamienia liście na jedną siatkę objętości korony (marching tetrahedra)."));
        if (!so.FindProperty("useLeafVolumeLods").boolValue)
            return;

        EditorGUILayout.PropertyField(so.FindProperty("leafVolumeStartLodLevel"),
            new GUIContent("Start od LOD", "Od którego poziomu LOD zastępować liście bryłą objętościową."));
        EditorGUILayout.PropertyField(so.FindProperty("leafVolumeGridResolution"),
            new GUIContent("Rozdzielczość siatki", "Rozdzielczość voxel grid dla ekstrakcji powierzchni. Więcej = dokładniej i wolniej."));
        EditorGUILayout.PropertyField(so.FindProperty("leafVolumeSampleRadiusInVoxels"),
            new GUIContent("Promień próbkowania", "Jak mocno liście wypełniają pole gęstości (w voxelach)."));
        EditorGUILayout.PropertyField(so.FindProperty("leafVolumeIsoLevel"),
            new GUIContent("Poziom izopowierzchni", "Niżej = większa, pełniejsza bryła; wyżej = bardziej ażurowa."));
        EditorGUILayout.PropertyField(so.FindProperty("leafVolumeSmoothIterations"),
            new GUIContent("Wygładzanie pola", "Liczba przejść wygładzania pola gęstości przed ekstrakcją."));
        EditorGUILayout.PropertyField(so.FindProperty("leafVolumeCloseFieldHoles"),
            new GUIContent("Załataj dziurki (pole)", "Zamykanie morfologiczne maski wewnątrz przed marching — mniej dziur w siatce korony."));
        if (so.FindProperty("leafVolumeCloseFieldHoles").boolValue)
        {
            EditorGUILayout.PropertyField(so.FindProperty("leafVolumeHoleCloseRadius"),
                new GUIContent("Promień zamykania", "Liczba przejść dylatacji + erozji (26-sąsiedztwo). Większe = szersze łatanie, grubsza korona."));
            EditorGUILayout.PropertyField(so.FindProperty("leafVolumeSmoothAfterHoleClose"),
                new GUIContent("Blur po łataniu", "Krótkie wygładzanie pola po zamknięciu, żeby złagodzić schodki."));
        }
        EditorGUILayout.PropertyField(so.FindProperty("leafVolumeBoundsPadding"),
            new GUIContent("Padding bounds", "Dodatkowy margines obszaru voxelizacji wokół liści."));
        EditorGUILayout.Space(4);
        EditorGUILayout.PropertyField(so.FindProperty("leafVolumeGeometryOptimize"),
            new GUIContent("Optymalizacja geometrii", "Po zbudowaniu bryły korony: weld, usuwanie zdegenerowanych trójkątów, deduplikacja."));
        var opt = so.FindProperty("leafVolumeGeometryOptimize");
        int optEnum = opt.enumValueIndex;
        bool needsWeld = optEnum == (int)LeafVolumeGeometryOptimizeMode.WeldVertices
                         || optEnum == (int)LeafVolumeGeometryOptimizeMode.WeldAndRemoveDegenerate
                         || optEnum == (int)LeafVolumeGeometryOptimizeMode.AggressiveWeld
                         || optEnum == (int)LeafVolumeGeometryOptimizeMode.WeldDegenerateAndDedupe;
        bool needsArea = optEnum != (int)LeafVolumeGeometryOptimizeMode.None
                         && optEnum != (int)LeafVolumeGeometryOptimizeMode.WeldVertices;
        if (needsWeld)
            EditorGUILayout.PropertyField(so.FindProperty("leafVolumeWeldEpsilon"),
                new GUIContent("Weld epsilon", "Rozmiar komórki siatki łączenia wierzchołków korony (jednostki świata)."));
        if (needsArea)
            EditorGUILayout.PropertyField(so.FindProperty("leafVolumeMinTriangleAreaSq"),
                new GUIContent("Min pole trójkąta (kw)", "0 = domyślny próg. Większe = agresywniejsze wycinanie cienkich trójkątów."));
    }

    private static void DrawLodTabLeavesAndClustering(SerializedObject so)
    {
        EditorGUILayout.PropertyField(so.FindProperty("enableLodInnerLeafClustering"),
            new GUIContent("Włącz grupowanie w środku", "Grupy liści w centralnej strefie korony oraz tłumienie rozmiaru liści u góry korony na LOD. Wyłączenie wyłącza oba efekty."));
        if (!so.FindProperty("enableLodInnerLeafClustering").boolValue)
        {
            EditorGUILayout.HelpBox(
                "Przy wyłączonym grupowaniu nie stosuje się tłumienia góry korony ani podmiany liści na grupy.",
                MessageType.Info);
            return;
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Grupowanie liści wewnątrz korony", EditorStyles.boldLabel);
        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("Tłumienie góry korony na LOD", EditorStyles.miniBoldLabel);
        EditorGUILayout.PropertyField(so.FindProperty("lod1TopLeafSizeDamping"),
            new GUIContent("LOD1: tłumienie góry korony", "1 = brak tłumienia góry korony, 0 = mocne tłumienie dla LOD1."));
        EditorGUILayout.PropertyField(so.FindProperty("lod2TopLeafSizeDamping"),
            new GUIContent("LOD2: tłumienie góry korony", "1 = brak tłumienia góry korony, 0 = mocne tłumienie dla LOD2."));
        EditorGUILayout.PropertyField(so.FindProperty("lod3TopLeafSizeDamping"),
            new GUIContent("LOD3: tłumienie góry korony", "1 = brak tłumienia góry korony, 0 = mocne tłumienie dla LOD3."));
        EditorGUILayout.PropertyField(so.FindProperty("lod4TopLeafSizeDamping"),
            new GUIContent("LOD4: tłumienie góry korony", "1 = brak tłumienia góry korony, 0 = mocne tłumienie dla LOD4+."));
        EditorGUILayout.PropertyField(so.FindProperty("lodTopLeafCurveInfluence"),
            new GUIContent("Wpływ krzywej tłumienia", "Mnożnik wpływu krzywej na profil tłumienia (0 = brak wpływu, >1 = mocniejszy wpływ)."));
        EditorGUILayout.PropertyField(so.FindProperty("lodTopLeafDampingByHeight"),
            new GUIContent("Krzywa tłumienia wg wysokości", "Steruje profilem tłumienia od dołu (X=0) do góry korony (X=1). Wartości Y > 1 wzmacniają efekt."));
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Strefa środka i podmiana na grupy", EditorStyles.miniBoldLabel);

        EditorGUILayout.PropertyField(so.FindProperty("lodInnerClusterRangeMin01"),
            new GUIContent("Strefa środka: min", "Znormalizowana odległość od pnia (0 = przy pniu, 1 = obrzeża korony)."));
        EditorGUILayout.PropertyField(so.FindProperty("lodInnerClusterRangeMax01"),
            new GUIContent("Strefa środka: max", "Górny zakres odległości od pnia, w którym działa grupowanie."));
        EditorGUILayout.PropertyField(so.FindProperty("lodInnerClusterByHeight"),
            new GUIContent("Krzywa grupowania wg wysokości", "Krzywa siły grupowania po wysokości drzewa (X=0 dół, X=1 góra). Y=0 wyłącza grupowanie na danej wysokości, Y>1 je wzmacnia."));
        EditorGUILayout.Space(2);
        EditorGUILayout.PropertyField(so.FindProperty("lod1InnerClusterReplacement"),
            new GUIContent("LOD1: podmiana na grupy", "Udział pojedynczych liści zamienianych na grupy."));
        EditorGUILayout.PropertyField(so.FindProperty("lod2InnerClusterReplacement"),
            new GUIContent("LOD2: podmiana na grupy"));
        EditorGUILayout.PropertyField(so.FindProperty("lod3InnerClusterReplacement"),
            new GUIContent("LOD3: podmiana na grupy"));
        EditorGUILayout.PropertyField(so.FindProperty("lod4InnerClusterReplacement"),
            new GUIContent("LOD4+: podmiana na grupy"));
        EditorGUILayout.Space(2);
        EditorGUILayout.PropertyField(so.FindProperty("lod1InnerClusterGroupLeaves"),
            new GUIContent("LOD1: liści na grupę", "Ile pojedynczych liści jest reprezentowane przez 1 większy trójkąt."));
        EditorGUILayout.PropertyField(so.FindProperty("lod2InnerClusterGroupLeaves"),
            new GUIContent("LOD2: liści na grupę"));
        EditorGUILayout.PropertyField(so.FindProperty("lod3InnerClusterGroupLeaves"),
            new GUIContent("LOD3: liści na grupę"));
        EditorGUILayout.PropertyField(so.FindProperty("lod4InnerClusterGroupLeaves"),
            new GUIContent("LOD4+: liści na grupę"));
        EditorGUILayout.Space(2);
        EditorGUILayout.PropertyField(so.FindProperty("lod1InnerClusterSizeMultiplier"),
            new GUIContent("LOD1: mnożnik rozmiaru grupy", "Dodatkowe powiększenie geometrii grup liści."));
        EditorGUILayout.PropertyField(so.FindProperty("lod2InnerClusterSizeMultiplier"),
            new GUIContent("LOD2: mnożnik rozmiaru grupy"));
        EditorGUILayout.PropertyField(so.FindProperty("lod3InnerClusterSizeMultiplier"),
            new GUIContent("LOD3: mnożnik rozmiaru grupy"));
        EditorGUILayout.PropertyField(so.FindProperty("lod4InnerClusterSizeMultiplier"),
            new GUIContent("LOD4+: mnożnik rozmiaru grupy"));
        EditorGUILayout.PropertyField(so.FindProperty("debugTintClusteredLeaves"),
            new GUIContent("Debug: czerwony tint klastrów", "Koloruje liście-klastry na czerwono, aby wizualnie sprawdzić gdzie działa grupowanie."));
    }

    private static void DrawLodTabLightmap(SerializedObject so)
    {
        EditorGUILayout.LabelField("Scale In Lightmap per LOD", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(so.FindProperty("autoLodScaleInLightmap"),
            new GUIContent("Auto Scale In Lightmap", "Automatycznie ustawia Scale In Lightmap dla każdego LOD proporcjonalnie do złożoności geometrii."));
        if (!so.FindProperty("autoLodScaleInLightmap").boolValue)
            return;

        EditorGUILayout.PropertyField(so.FindProperty("manualLodScaleInLightmap"),
            new GUIContent("Ręczna skala per LOD", "Pozwala wpisać Scale In Lightmap osobno dla każdego poziomu LOD."));
        if (so.FindProperty("manualLodScaleInLightmap").boolValue)
        {
            EditorGUILayout.PropertyField(so.FindProperty("lod0ScaleInLightmap"),
                new GUIContent("LOD0 Scale In Lightmap"));
            EditorGUILayout.PropertyField(so.FindProperty("lod1ScaleInLightmap"),
                new GUIContent("LOD1 Scale In Lightmap"));
            EditorGUILayout.PropertyField(so.FindProperty("lod2ScaleInLightmap"),
                new GUIContent("LOD2 Scale In Lightmap"));
            EditorGUILayout.PropertyField(so.FindProperty("lod3ScaleInLightmap"),
                new GUIContent("LOD3 Scale In Lightmap"));
            EditorGUILayout.PropertyField(so.FindProperty("lod4ScaleInLightmap"),
                new GUIContent("LOD4+ Scale In Lightmap"));
        }
        else
        {
            EditorGUILayout.PropertyField(so.FindProperty("lodMinScaleInLightmap"),
                new GUIContent("Min Scale In Lightmap", "Minimalna wartość Scale In Lightmap dla dalszych LOD."));
            EditorGUILayout.PropertyField(so.FindProperty("lodLightmapScalePower"),
                new GUIContent("Krzywa skali lightmap", "Kształt proporcji Scale In Lightmap względem liczby trójkątów LOD."));
            EditorGUILayout.PropertyField(so.FindProperty("lodLightmapScaleUseWoodOnly"),
                new GUIContent("Skaluj wg drewna", "Gdy włączone, proporcja Scale In Lightmap liczona jest tylko z trójkątów drewna (bez liści), co zwykle daje stabilniejszy wynik."));
            EditorGUILayout.PropertyField(so.FindProperty("lodLightmapScaleBiasToLod0"),
                new GUIContent("Bias do LOD0", "Dodatkowo zbliża Scale In Lightmap do 1.0 dla dalszych LOD (większa wartość = wolniejsza degradacja)."));
        }
    }
}
