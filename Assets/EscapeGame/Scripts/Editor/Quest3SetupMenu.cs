using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace EscapeGame.Editor
{
    /// <summary>
    /// Menus "Escape Game > Quest 3" : réglages de rendu adaptés au Meta Quest 3 (APK Android)
    /// et préparation de l'éclairage précalculé (lightmaps) de la scène ouverte.
    /// </summary>
    static class Quest3SetupMenu
    {
        const string k_QuestQualityLevel = "Low"; // niveau utilisé par défaut pour Android dans ce projet
        const string k_QuestPipelinePath = "Assets/Settings/Project Configuration/Performance URP Config.asset";

        // Grille de light probes (éclairage des objets qui bougent).
        const float k_ProbeSpacing = 1.5f;
        static readonly float[] k_ProbeHeights = { 0.3f, 0.9f, 1.5f, 2.1f }; // au-dessus du sol (m)

        [MenuItem("Escape Game/Quest 3/1. Réglages de rendu du projet")]
        static void ApplyProjectSettings()
        {
            var report = new StringBuilder();

            // Niveau de qualité "Low" actif dans l'éditeur : on voit ce que verra le casque.
            var level = System.Array.IndexOf(QualitySettings.names, k_QuestQualityLevel);
            if (level >= 0)
            {
                QualitySettings.SetQualityLevel(level, true);
                report.AppendLine($"• Niveau de qualité actif : {k_QuestQualityLevel}");
            }

            var pipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(k_QuestPipelinePath);
            if (pipeline == null)
            {
                EditorUtility.DisplayDialog("Quest 3", $"Introuvable : {k_QuestPipelinePath}", "OK");
                return;
            }

            var so = new SerializedObject(pipeline);
            Set(so, "m_AdditionalLightsRenderingMode", 1, report, "Lumières supplémentaires : par pixel"); // 1 = Per Pixel
            Set(so, "m_AdditionalLightsPerObjectLimit", 4, report, "Lumières par objet : 4");
            Set(so, "m_AdditionalLightShadowsSupported", 0, report, "Ombres temps réel des lumières : non (précalculées)");
            Set(so, "m_MainLightShadowsSupported", 0, report, "Ombres du soleil : non (pas de soleil)");
            Set(so, "m_SupportsHDR", 0, report, "HDR : non");
            Set(so, "m_MSAA", 4, report, "Anticrénelage : MSAA 4x");
            Set(so, "m_ReflectionProbeBlending", 0, report, "Mélange des reflection probes : non");
            Set(so, "m_ReflectionProbeBoxProjection", 1, report, "Box projection des reflection probes : oui");
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pipeline);
            AssetDatabase.SaveAssets();

            var android = EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android;
            report.AppendLine();
            report.AppendLine(android
                ? "La plateforme active est Android : l'éditeur affiche le rendu du casque."
                : "La plateforme active n'est pas Android. Installe le module « Android Build Support » " +
                  "(Unity Hub > Installs > ⚙ > Add modules), puis File > Build Profiles > Android > Switch Platform.");

            Debug.Log("Réglages Quest 3 appliqués :\n" + report);
            EditorUtility.DisplayDialog("Quest 3 - réglages du projet", report.ToString(), "OK");
        }

        static void Set(SerializedObject so, string property, int value, StringBuilder report, string label)
        {
            var p = so.FindProperty(property);
            if (p == null)
            {
                report.AppendLine($"• (réglage introuvable : {property})");
                return;
            }
            if (p.propertyType == SerializedPropertyType.Boolean)
                p.boolValue = value != 0;
            else
                p.intValue = value;
            report.AppendLine("• " + label);
        }

        [MenuItem("Escape Game/Quest 3/2. Préparer l'éclairage précalculé (scène ouverte)")]
        static void PrepareBakedLighting()
        {
            var scene = SceneManager.GetActiveScene();
            var renderers = FindStaticCandidates(scene);
            var lights = scene.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<Light>(true))
                .Where(l => l.type != LightType.Directional)
                .ToList();

            if (!EditorUtility.DisplayDialog("Quest 3 - éclairage précalculé",
                    $"Scène « {scene.name} » :\n" +
                    $"• {renderers.Count} objets de décor marqués Static, ombres double face (murs étanches à la lumière)\n" +
                    $"• {lights.Count} lumières passées en Baked (leurs autres réglages ne changent pas)\n" +
                    "• UV de lightmap générés sur les modèles 3D du décor (réimport, peut prendre une minute)\n" +
                    "• Réglages de lightmap adaptés au Quest\n" +
                    "• Grille de light probes pour les objets qui bougent\n\n" +
                    "Tout est annulable avec Ctrl+Z, sauf le réimport des modèles. Continuer ?", "Continuer", "Annuler"))
                return;

            var report = new StringBuilder();

            // 1. Décor statique.
            Undo.RecordObjects(renderers.Select(r => (Object)r.gameObject).ToArray(), "Décor statique");
            const StaticEditorFlags flags = StaticEditorFlags.ContributeGI | StaticEditorFlags.ReflectionProbeStatic |
                                            StaticEditorFlags.BatchingStatic;
            Undo.RecordObjects(renderers.Select(r => (Object)r).ToArray(), "Ombres double face");
            foreach (var r in renderers)
            {
                GameObjectUtility.SetStaticEditorFlags(r.gameObject,
                    GameObjectUtility.GetStaticEditorFlags(r.gameObject) | flags);
                // Les murs venus de Blender n'ont souvent qu'une face : vus de dos, ils laisseraient passer la lumière.
                // Ombres "double face" : les deux côtés bloquent la lumière (sans coût en jeu, elle est précalculée).
                r.shadowCastingMode = ShadowCastingMode.TwoSided;
            }
            report.AppendLine($"• {renderers.Count} objets de décor marqués Static, ombres double face");

            // 2. UV de lightmap sur les FBX du décor (sinon taches et fuites de lumière).
            var reimported = GenerateLightmapUVs(renderers);
            report.AppendLine(reimported > 0
                ? $"• UV de lightmap générés sur {reimported} modèle(s) 3D"
                : "• UV de lightmap : déjà en place");

            // 3. Lumières précalculées : seul le mode change, angle, intensité, rebond, couleur... restent ceux réglés à la main.
            Undo.RecordObjects(lights.Select(l => (Object)l).ToArray(), "Lumières précalculées");
            foreach (var light in lights)
                light.lightmapBakeType = LightmapBakeType.Baked;
            report.AppendLine($"• {lights.Count} lumière(s) en Baked, autres réglages inchangés");

            // Une lumière sans ombre traverse les murs : on le signale sans rien changer.
            var noShadow = lights.Where(l => l.shadows == LightShadows.None).Select(l => l.name).ToList();
            if (noShadow.Count > 0)
                report.AppendLine($"  ⚠ Sans ombre (traversent les murs) : {string.Join(", ", noShadow)}. Mettre Shadow Type sur Soft Shadows.");

            // 4. Réglages de lightmap.
            var settings = GetOrCreateLightingSettings(scene);
            Undo.RecordObject(settings, "Réglages de lightmap");
            // Seulement la première fois : ensuite, les réglages faits à la main dans la fenêtre Lighting sont conservés.
            if (!settings.bakedGI)
            {
                settings.bakedGI = true;
                settings.realtimeGI = false;
                settings.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
                settings.lightmapResolution = 20f;
                settings.lightmapMaxSize = 2048;
                settings.directionalityMode = LightmapsMode.NonDirectional;
                settings.mixedBakeMode = MixedLightingMode.IndirectOnly;
                settings.directSampleCount = 64;
                settings.indirectSampleCount = 512;
                settings.maxBounces = 2;
                settings.ao = true;
                settings.aoMaxDistance = 1f;
                EditorUtility.SetDirty(settings);
                report.AppendLine("• Lightmaps : GPU, 20 texels/m, occlusion ambiante, 2048 px max");
            }
            else
                report.AppendLine("• Lightmaps : réglages existants conservés");

            // 5. Light probes.
            var probes = CreateLightProbeGrid(scene, renderers);
            report.AppendLine($"• Light probes : {probes} points au-dessus du sol, hors des murs et des meubles");

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("Éclairage précalculé préparé :\n" + report);

            if (EditorUtility.DisplayDialog("Quest 3 - éclairage précalculé",
                    report + "\nLancer le calcul de l'éclairage maintenant ? (quelques minutes, progression en bas à droite)",
                    "Lancer", "Plus tard"))
                Lightmapping.BakeAsync();
        }

        // Objets du décor : tout ce qui est affiché par un MeshRenderer et ne bouge pas.
        static List<MeshRenderer> FindStaticCandidates(Scene scene)
        {
            var result = new List<MeshRenderer>();
            foreach (var root in scene.GetRootGameObjects())
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.GetComponentInParent<Rigidbody>(true) != null ||          // objets physiques / attrapables
                    r.GetComponentInParent<XRBaseInteractable>(true) != null ||
                    r.GetComponentInParent<XROrigin>(true) != null ||           // joueur, manettes
                    r.GetComponentInParent<Canvas>(true) != null ||             // écrans d'énigme
                    r.GetComponent<TMP_Text>() != null ||                       // textes 3D
                    r.GetComponent<WritableSurface>() != null ||                // zone d'écriture (shader non éclairé)
                    r.GetComponentInParent<BoardWordPuzzle>(true) != null)      // traits et coche (non éclairés)
                    continue;
                result.Add(r);
            }
            return result;
        }

        static int GenerateLightmapUVs(List<MeshRenderer> renderers)
        {
            var paths = new HashSet<string>();
            foreach (var r in renderers)
            {
                var filter = r.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null)
                    paths.Add(AssetDatabase.GetAssetPath(filter.sharedMesh));
            }

            var count = 0;
            foreach (var path in paths)
            {
                if (!(AssetImporter.GetAtPath(path) is ModelImporter importer) || importer.generateSecondaryUV)
                    continue;
                importer.generateSecondaryUV = true;
                importer.SaveAndReimport();
                count++;
            }
            return count;
        }

        static LightingSettings GetOrCreateLightingSettings(Scene scene)
        {
            if (Lightmapping.TryGetLightingSettings(out var settings) && settings != null)
                return settings;

            settings = new LightingSettings { name = scene.name + " - Lighting" };
            var folder = System.IO.Path.GetDirectoryName(scene.path)?.Replace('\\', '/') ?? "Assets";
            AssetDatabase.CreateAsset(settings, AssetDatabase.GenerateUniqueAssetPath($"{folder}/{settings.name}.lighting"));
            Lightmapping.lightingSettings = settings;
            return settings;
        }

        [MenuItem("Escape Game/Quest 3/3. Recréer les light probes (scène ouverte)")]
        static void RebuildLightProbes()
        {
            var scene = SceneManager.GetActiveScene();
            var count = CreateLightProbeGrid(scene, FindStaticCandidates(scene));
            if (EditorUtility.DisplayDialog("Light probes",
                    $"{count} light probes placés au-dessus du sol, hors des murs et des meubles.\n\n" +
                    "Relancer le calcul de l'éclairage maintenant ?", "Lancer", "Plus tard"))
                Lightmapping.BakeAsync();
        }

        // Grille de light probes (éclairage des objets qui bougent), posée sur le sol où se tient le joueur.
        // Les points qui tombent dans un mur ou un meuble sont retirés (ils seraient noirs).
        // Remplace la grille créée précédemment par ce menu ("Light Probes").
        static int CreateLightProbeGrid(Scene scene, List<MeshRenderer> renderers)
        {
            if (renderers.Count == 0)
                return 0;

            // Sol = hauteur du XR Origin (les pieds du joueur), sinon le bas du décor.
            var origin = scene.GetRootGameObjects().Select(r => r.GetComponentInChildren<XROrigin>(true)).FirstOrDefault(o => o != null);
            var floor = origin != null ? origin.transform.position.y : renderers.Min(r => r.bounds.min.y);

            // Étendue de la pièce : le décor situé entre le sol et le plafond.
            var inRoom = renderers.Where(r => r.bounds.max.y > floor - 0.5f && r.bounds.min.y < floor + 3.5f).ToList();
            if (inRoom.Count == 0)
                return 0;
            var bounds = inRoom[0].bounds;
            foreach (var r in inRoom)
                bounds.Encapsulate(r.bounds);

            var go = scene.GetRootGameObjects().FirstOrDefault(r => r.name == "Light Probes" && r.GetComponent<LightProbeGroup>() != null);
            if (go == null)
            {
                go = new GameObject("Light Probes");
                SceneManager.MoveGameObjectToScene(go, scene);
                Undo.RegisterCreatedObjectUndo(go, "Light probes");
                go.AddComponent<LightProbeGroup>();
            }
            var group = go.GetComponent<LightProbeGroup>();
            Undo.RecordObject(group, "Light probes");
            Undo.RecordObject(go.transform, "Light probes");
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = Vector3.one;

            // Grande scène : on espace les points pour en garder un nombre raisonnable.
            var spacing = k_ProbeSpacing;
            while (bounds.size.x / spacing * (bounds.size.z / spacing) * k_ProbeHeights.Length > 2000f)
                spacing *= 1.25f;

            Physics.SyncTransforms();
            var positions = new List<Vector3>();
            for (var x = bounds.min.x + spacing / 2f; x < bounds.max.x; x += spacing)
            for (var z = bounds.min.z + spacing / 2f; z < bounds.max.z; z += spacing)
            foreach (var h in k_ProbeHeights)
            {
                var p = new Vector3(x, floor + h, z);
                if (!Physics.CheckSphere(p, 0.08f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    positions.Add(p);
            }
            group.probePositions = positions.ToArray();
            EditorUtility.SetDirty(group);
            return positions.Count;
        }
    }
}

