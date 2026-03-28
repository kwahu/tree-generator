using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TreeGenerator))]
public class TreeGeneratorEditor : Editor
{
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
        EditorGUILayout.PropertyField(serializedObject.FindProperty("autoUpdateWhenValid"),
            new GUIContent("Auto aktualizacja",
                "Automatycznie przebudowuje drzewo podczas edycji parametrów, tylko jeśli są poprawne."));
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("LOD", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("autoGenerateLods"),
            new GUIContent("Auto LOD", "Automatycznie tworzy i konfiguruje LODGroup oraz poziomy LOD."));
        if (serializedObject.FindProperty("autoGenerateLods").boolValue)
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("lodLevels"),
                new GUIContent("Poziomy LOD", "Liczba poziomów LOD (wliczając LOD0)."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("lodStartScreenRelativeHeight"),
                new GUIContent("Start ekranu LOD0", "Screen Relative Transition Height dla LOD0."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("lodEndScreenRelativeHeight"),
                new GUIContent("Koniec ekranu ostatniego LOD", "Screen Relative Transition Height dla najdalszego LOD."));
            EditorGUILayout.Space(2);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("lodFinalLeafCountMultiplier"),
                new GUIContent("Liście: końcowy mnożnik ilości", "Agresywna redukcja ilości liści na najdalszym LOD."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("lodFinalLeafSizeMultiplier"),
                new GUIContent("Liście: końcowy mnożnik rozmiaru", "Powiększenie liści na najdalszym LOD dla zachowania wizualnej masy."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("lodLeafReductionExponent"),
                new GUIContent("Liście: agresywność redukcji", "Wyższa wartość = szybsza redukcja liści na wcześniejszych LOD."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("lod1TopLeafSizeDamping"),
                new GUIContent("LOD1: tłumienie góry korony", "1 = brak tłumienia góry korony, 0 = mocne tłumienie dla LOD1."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("lod2TopLeafSizeDamping"),
                new GUIContent("LOD2: tłumienie góry korony", "1 = brak tłumienia góry korony, 0 = mocne tłumienie dla LOD2."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("lod3TopLeafSizeDamping"),
                new GUIContent("LOD3: tłumienie góry korony", "1 = brak tłumienia góry korony, 0 = mocne tłumienie dla LOD3."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("lod4TopLeafSizeDamping"),
                new GUIContent("LOD4: tłumienie góry korony", "1 = brak tłumienia góry korony, 0 = mocne tłumienie dla LOD4+."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("lodTopLeafCurveInfluence"),
                new GUIContent("Wpływ krzywej tłumienia", "Mnożnik wpływu krzywej na profil tłumienia (0 = brak wpływu, >1 = mocniejszy wpływ)."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("lodTopLeafDampingByHeight"),
                new GUIContent("Krzywa tłumienia wg wysokości", "Steruje profilem tłumienia od dołu (X=0) do góry korony (X=1). Wartości Y > 1 wzmacniają efekt."));
            EditorGUILayout.Space(2);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("enableLodInnerLeafClustering"),
                new GUIContent("LOD: grupowanie liści w środku", "Zastępuje część pojedynczych liści większymi grupami w centralnej strefie korony."));
            if (serializedObject.FindProperty("enableLodInnerLeafClustering").boolValue)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("lodInnerClusterRangeMin01"),
                    new GUIContent("Strefa środka: min", "Znormalizowana odległość od pnia (0 = przy pniu, 1 = obrzeża korony)."));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("lodInnerClusterRangeMax01"),
                    new GUIContent("Strefa środka: max", "Górny zakres odległości od pnia, w którym działa grupowanie."));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("lodInnerClusterByHeight"),
                    new GUIContent("Krzywa grupowania wg wysokości", "Krzywa siły grupowania po wysokości drzewa (X=0 dół, X=1 góra). Y=0 wyłącza grupowanie na danej wysokości, Y>1 je wzmacnia."));
                EditorGUILayout.Space(1);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("lod1InnerClusterReplacement"),
                    new GUIContent("LOD1: podmiana na grupy", "Udział pojedynczych liści zamienianych na grupy."));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("lod2InnerClusterReplacement"),
                    new GUIContent("LOD2: podmiana na grupy"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("lod3InnerClusterReplacement"),
                    new GUIContent("LOD3: podmiana na grupy"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("lod4InnerClusterReplacement"),
                    new GUIContent("LOD4+: podmiana na grupy"));
                EditorGUILayout.Space(1);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("lod1InnerClusterGroupLeaves"),
                    new GUIContent("LOD1: liści na grupę", "Ile pojedynczych liści jest reprezentowane przez 1 większy trójkąt."));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("lod2InnerClusterGroupLeaves"),
                    new GUIContent("LOD2: liści na grupę"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("lod3InnerClusterGroupLeaves"),
                    new GUIContent("LOD3: liści na grupę"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("lod4InnerClusterGroupLeaves"),
                    new GUIContent("LOD4+: liści na grupę"));
                EditorGUILayout.Space(1);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("lod1InnerClusterSizeMultiplier"),
                    new GUIContent("LOD1: mnożnik rozmiaru grupy", "Dodatkowe powiększenie geometrii grup liści."));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("lod2InnerClusterSizeMultiplier"),
                    new GUIContent("LOD2: mnożnik rozmiaru grupy"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("lod3InnerClusterSizeMultiplier"),
                    new GUIContent("LOD3: mnożnik rozmiaru grupy"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("lod4InnerClusterSizeMultiplier"),
                    new GUIContent("LOD4+: mnożnik rozmiaru grupy"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("debugTintClusteredLeaves"),
                    new GUIContent("Debug: czerwony tint klastrów", "Koloruje liście-klastry na czerwono, aby wizualnie sprawdzić gdzie działa grupowanie."));
            }
            EditorGUILayout.Space(2);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("autoLodScaleInLightmap"),
                new GUIContent("Auto Scale In Lightmap", "Automatycznie ustawia Scale In Lightmap dla każdego LOD proporcjonalnie do złożoności geometrii."));
            if (serializedObject.FindProperty("autoLodScaleInLightmap").boolValue)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("manualLodScaleInLightmap"),
                    new GUIContent("Ręczna skala per LOD", "Pozwala wpisać Scale In Lightmap osobno dla każdego poziomu LOD."));
                if (serializedObject.FindProperty("manualLodScaleInLightmap").boolValue)
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("lod0ScaleInLightmap"),
                        new GUIContent("LOD0 Scale In Lightmap"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("lod1ScaleInLightmap"),
                        new GUIContent("LOD1 Scale In Lightmap"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("lod2ScaleInLightmap"),
                        new GUIContent("LOD2 Scale In Lightmap"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("lod3ScaleInLightmap"),
                        new GUIContent("LOD3 Scale In Lightmap"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("lod4ScaleInLightmap"),
                        new GUIContent("LOD4+ Scale In Lightmap"));
                }
                else
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("lodMinScaleInLightmap"),
                        new GUIContent("Min Scale In Lightmap", "Minimalna wartość Scale In Lightmap dla dalszych LOD."));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("lodLightmapScalePower"),
                        new GUIContent("Krzywa skali lightmap", "Kształt proporcji Scale In Lightmap względem liczby trójkątów LOD."));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("lodLightmapScaleUseWoodOnly"),
                        new GUIContent("Skaluj wg drewna", "Gdy włączone, proporcja Scale In Lightmap liczona jest tylko z trójkątów drewna (bez liści), co zwykle daje stabilniejszy wynik."));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("lodLightmapScaleBiasToLod0"),
                        new GUIContent("Bias do LOD0", "Dodatkowo zbliża Scale In Lightmap do 1.0 dla dalszych LOD (większa wartość = wolniejsza degradacja)."));
                }
            }
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
}
