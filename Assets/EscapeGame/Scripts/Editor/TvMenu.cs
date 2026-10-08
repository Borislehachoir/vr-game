using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Attachment;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace EscapeGame.Editor
{
    /// <summary>
    /// Menus "GameObject > Escape Game > Télécommande" et "Écran de télé" :
    /// la télécommande Freebox (avec ses couleurs) qu'on attrape et dont les boutons A/B ou X/Y allument la télé,
    /// et l'écran posé sur la dalle de la télé sélectionnée.
    /// </summary>
    static class TvMenu
    {
        const string k_RemoteFolder = "Assets/3D/Telecommande/telecommande-freebox-v6";
        const string k_RemoteModel = k_RemoteFolder + "/source/Telecommande Freebox_V6/Telecommande Freebox_V6.obj";
        const string k_RemoteMtl = k_RemoteFolder + "/source/Telecommande Freebox_V6/Telecommande_Freebox_V6.mtl";
        const string k_RemoteMetalTexture = k_RemoteFolder + "/textures/metallic-214511_640.jpg";
        const string k_RemoteMaterialFolder = "Assets/3D/Telecommande/Materials";
        const float k_RemoteLength = 0.21f; // longueur d'une télécommande Freebox réelle (m)

        // Modèle "Modern_TV" (mesuré dans Blender) : dalle de 1,604 m de large, écran 16:9 sur la face avant.
        const float k_TvWidth = 1.604f;
        const float k_TvScreenWidth = 1.116f, k_TvScreenHeight = 0.627f, k_TvScreenCenterHeight = 0.4765f;
        const float k_TvFrontDepth = -0.025f; // face avant (côté -X dans Blender)
        const float k_ScreenOffset = 0.003f;  // écran posé 3 mm devant la dalle

        // ---- Télécommande ----

        [MenuItem("GameObject/Escape Game/Télécommande (allume la télé)", false, 15)]
        static void CreateRemote(MenuCommand command)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(k_RemoteModel);
            if (model == null)
            {
                EditorUtility.DisplayDialog("Télécommande", $"Modèle introuvable : {k_RemoteModel}", "OK");
                return;
            }
            ApplyRemoteMaterials();

            // Toujours à la racine de la scène : un objet attrapable rangé dans un modèle importé (souvent à l'échelle ×100)
            // prendrait cette échelle quand XRI le sort de son parent en le prenant en main.
            var root = new GameObject("Télécommande");
            WritingBoardMenu.PlaceInFrontOfSceneView(root, new MenuCommand(null));
            if (command.context is GameObject clicked)
                root.transform.position = clicked.transform.position + Vector3.up * 0.5f; // à côté de l'objet cliqué
            var rotation = root.transform.rotation;
            root.transform.rotation = Quaternion.identity;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            instance.name = "Modèle";

            // Le modèle est penché d'environ 10° dans son fichier : on le redresse, bout "marche/arrêt" vers +Z, touches vers +Y.
            AlignRemote(instance, root.transform);

            // Mise à l'échelle réelle et centrage (le modèle est exporté en centimètres, décentré).
            var bounds = WorldBounds(instance);
            var scale = k_RemoteLength / Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            instance.transform.localScale *= scale;
            bounds = WorldBounds(instance);
            instance.transform.position += root.transform.position - bounds.center;
            bounds = WorldBounds(instance);

            var collider = root.AddComponent<BoxCollider>();
            collider.center = root.transform.InverseTransformPoint(bounds.center);
            collider.size = bounds.size;

            var body = root.AddComponent<Rigidbody>();
            body.mass = 0.15f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            var grab = root.AddComponent<XRGrabInteractable>();
            // Prise fixe : dans la paume, touches vers le haut, bout "marche/arrêt" pointé devant soi.
            var attach = new GameObject("Prise en main").transform;
            attach.SetParent(root.transform, false);
            attach.localPosition = collider.center + new Vector3(0f, -collider.size.y * 0.2f, -collider.size.z * 0.2f);
            grab.attachTransform = attach;
            grab.useDynamicAttach = false;
            grab.farAttachMode = InteractableFarAttachMode.Near;

            var remote = root.AddComponent<TvRemote>();
            remote.tv = Object.FindAnyObjectByType<TvScreen>();
            root.AddComponent<ReturnToStartOnFloor>();

            root.transform.rotation = rotation;
            Undo.RegisterCreatedObjectUndo(root, "Créer télécommande");
            Selection.activeGameObject = root;

            if (remote.tv == null)
                EditorUtility.DisplayDialog("Télécommande", "Aucune télé avec écran dans la scène : sélectionne la télé puis " +
                    "Escape Game > Écran de télé. La télécommande la trouvera automatiquement.", "OK");
        }

        // Axe de la télécommande (direction principale de ses sommets, à plat) et sens : le bas est la partie métal ("Silver").
        static void AlignRemote(GameObject model, Transform root)
        {
            var all = new List<Vector3>();
            var bottom = new List<Vector3>();
            var buttons = new List<Vector3>();
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = filter.sharedMesh;
                var renderer = filter.GetComponent<Renderer>();
                if (mesh == null || renderer == null)
                    continue;
                var vertices = mesh.vertices;
                for (var sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    var material = sub < renderer.sharedMaterials.Length ? renderer.sharedMaterials[sub] : null;
                    var name = material != null ? material.name.ToLowerInvariant() : "";
                    foreach (var index in mesh.GetTriangles(sub))
                    {
                        var p = root.InverseTransformPoint(filter.transform.TransformPoint(vertices[index]));
                        all.Add(p);
                        if (name.Contains("silver"))
                            bottom.Add(p);
                        else if (name.Contains("rubber"))
                            buttons.Add(p);
                    }
                }
            }
            if (all.Count == 0)
                return;

            var center = Average(all);
            // Direction principale dans le plan horizontal (analyse en composantes principales 2D).
            float xx = 0f, xz = 0f, zz = 0f;
            foreach (var p in all)
            {
                var d = p - center;
                xx += d.x * d.x; xz += d.x * d.z; zz += d.z * d.z;
            }
            var angle = 0.5f * Mathf.Atan2(2f * xz, xx - zz);
            var axis = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            if (bottom.Count > 0 && Vector3.Dot(Average(bottom) - center, axis) > 0f)
                axis = -axis; // axe orienté vers le haut de la télécommande (opposé à la partie métal)

            model.transform.rotation = Quaternion.FromToRotation(axis, Vector3.forward) * model.transform.rotation;
            if (buttons.Count > 0 && Average(buttons).y < center.y)
                model.transform.rotation = Quaternion.AngleAxis(180f, Vector3.forward) * model.transform.rotation; // touches dessous : on la retourne
        }

        static Vector3 Average(List<Vector3> points)
        {
            var sum = Vector3.zero;
            foreach (var p in points)
                sum += p;
            return sum / points.Count;
        }

        // Matériaux URP aux couleurs du fichier MTL (corps blanc, touches colorées, bordure métal).
        static void ApplyRemoteMaterials()
        {
            if (!(AssetImporter.GetAtPath(k_RemoteModel) is ModelImporter importer))
                return;

            var mtl = ReadMtl(k_RemoteMtl);
            if (!AssetDatabase.IsValidFolder(k_RemoteMaterialFolder))
                AssetDatabase.CreateFolder(Path.GetDirectoryName(k_RemoteMaterialFolder).Replace('\\', '/'), Path.GetFileName(k_RemoteMaterialFolder));

            var changed = false;
            foreach (var source in importer.GetExternalObjectMap().Keys.Concat(SourceMaterials(importer)).Distinct().ToList())
            {
                if (source.type != typeof(Material))
                    continue;
                var key = Normalize(source.name);
                var entry = mtl.FirstOrDefault(m => Normalize(m.Key) == key);
                var path = $"{k_RemoteMaterialFolder}/{Regex.Replace(source.name, @"[\\/:*?""<>|]", "_")}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    Configure(material, source.name, entry.Value);
                    AssetDatabase.CreateAsset(material, path);
                }
                importer.AddRemap(source, material);
                changed = true;
            }
            if (changed)
                importer.SaveAndReimport();
        }

        static IEnumerable<AssetImporter.SourceAssetIdentifier> SourceMaterials(ModelImporter importer)
        {
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(k_RemoteModel).OfType<Material>())
                yield return new AssetImporter.SourceAssetIdentifier(typeof(Material), asset.name);
        }

        static void Configure(Material material, string name, Color kd)
        {
            var lower = name.ToLowerInvariant();
            var color = kd == default ? new Color(0.9f, 0.9f, 0.9f) : kd;
            var smoothness = 0.45f;
            var metallic = 0f;
            if (lower.Contains("rubber"))
                smoothness = 0.3f;
            if (lower.Contains("silver"))
            {
                // Couleur nulle dans le MTL (texture procédurale de Cinema 4D) : métal brossé.
                color = new Color(0.8f, 0.8f, 0.82f);
                metallic = 1f;
                smoothness = 0.7f;
                var metal = AssetDatabase.LoadAssetAtPath<Texture2D>(k_RemoteMetalTexture);
                if (metal != null)
                    material.SetTexture("_BaseMap", metal);
            }
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metallic);
        }

        static Dictionary<string, Color> ReadMtl(string path)
        {
            var result = new Dictionary<string, Color>();
            if (!File.Exists(path))
                return result;
            string current = null;
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.StartsWith("newmtl "))
                {
                    current = line.Substring(7).Trim();
                    result[current] = default;
                }
                else if (line.StartsWith("Kd ") && current != null)
                {
                    var v = line.Substring(3).Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                    if (v.Length >= 3)
                        result[current] = new Color(float.Parse(v[0], culture), float.Parse(v[1], culture), float.Parse(v[2], culture));
                }
            }
            return result;
        }

        static string Normalize(string s) => Regex.Replace(s.ToLowerInvariant(), "[^a-z0-9]", "");

        static Bounds WorldBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            var bounds = renderers[0].bounds;
            foreach (var r in renderers)
                bounds.Encapsulate(r.bounds);
            return bounds;
        }

        // ---- Écran de télé ----

        [MenuItem("GameObject/Escape Game/Écran de télé (sur la télé sélectionnée)", false, 16)]
        static void CreateTvScreen(MenuCommand command)
        {
            var tv = command.context as GameObject ?? Selection.activeGameObject;
            var filter = tv != null
                ? tv.GetComponentsInChildren<MeshFilter>().Where(f => f.sharedMesh != null)
                    .OrderByDescending(f => f.sharedMesh.bounds.size.sqrMagnitude).FirstOrDefault()
                : null;
            if (filter == null)
            {
                EditorUtility.DisplayDialog("Écran de télé", "Sélectionne la télé dans la Hierarchy, puis relance le menu.", "OK");
                return;
            }
            if (tv.GetComponentInChildren<TvScreen>() != null)
            {
                EditorUtility.DisplayDialog("Écran de télé", "Cette télé a déjà un écran.", "OK");
                return;
            }

            // Axes du modèle : largeur = plus grande dimension, profondeur = plus petite, hauteur = l'autre.
            var b = filter.sharedMesh.bounds;
            var size = b.size;
            var axes = new[] { 0, 1, 2 }.OrderBy(i => size[i]).ToArray();
            int depth = axes[0], height = axes[1], width = axes[2];
            var k = size[width] / k_TvWidth; // unités du mesh par mètre du modèle

            // Sens des axes : le pied est en bas (hauteur ≥ 0) et la télé dépasse plus vers l'arrière que vers l'avant.
            var heightSign = b.max[height] >= -b.min[height] ? 1f : -1f;
            var depthSign = b.max[depth] >= -b.min[depth] ? 1f : -1f;

            var up = Vector3.zero;
            up[height] = heightSign;
            var front = Vector3.zero;
            front[depth] = -depthSign; // face avant = côté -X du modèle Blender

            var center = Vector3.zero;
            center[width] = b.center[width];
            center[height] = heightSign * k_TvScreenCenterHeight * k;
            center[depth] = depthSign * k_TvFrontDepth * k;
            var parentScale = Mathf.Abs(filter.transform.lossyScale.x);
            center += front * (k_ScreenOffset / Mathf.Max(parentScale, 1e-6f));

            var screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
            screen.name = "Écran";
            Object.DestroyImmediate(screen.GetComponent<Collider>());
            screen.transform.SetParent(filter.transform, false);
            screen.transform.localPosition = center;
            screen.transform.localRotation = Quaternion.LookRotation(-front, up); // face visible du Quad vers l'avant
            screen.transform.localScale = new Vector3(k_TvScreenWidth * k, k_TvScreenHeight * k, 1f);

            var off = WritingBoardMenu.GetOrCreateMaterial("Écran télé éteint", new Color(0.02f, 0.02f, 0.025f));
            off.SetFloat("_Smoothness", 0.95f);
            EditorUtility.SetDirty(off);
            var on = WritingBoardMenu.GetOrCreateMaterial("Écran télé allumé", Color.white, "Universal Render Pipeline/Unlit");

            var renderer = screen.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = off;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Lueur de l'écran sur la pièce : lumière temps réel, allumée avec la télé (pas d'ombre : légère sur Quest).
            var glowGo = new GameObject("Lueur écran");
            glowGo.transform.SetParent(screen.transform, false);
            glowGo.transform.position = screen.transform.position + filter.transform.TransformDirection(front).normalized * 0.6f;
            var glow = glowGo.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.lightmapBakeType = LightmapBakeType.Realtime;
            glow.range = 3f;
            glow.intensity = 1.5f;
            glow.color = new Color(0.75f, 0.85f, 1f);
            glow.shadows = LightShadows.None;
            glow.enabled = false;

            var tvScreen = screen.AddComponent<TvScreen>();
            tvScreen.screen = renderer;
            tvScreen.offMaterial = off;
            tvScreen.onMaterial = on;
            tvScreen.glow = glow;

            foreach (var remote in Object.FindObjectsByType<TvRemote>(FindObjectsSortMode.None).Where(r => r.tv == null))
            {
                Undo.RecordObject(remote, "Relier télécommande");
                remote.tv = tvScreen;
            }

            Undo.RegisterCreatedObjectUndo(screen, "Créer écran de télé");
            Selection.activeGameObject = screen;
        }

        [MenuItem("GameObject/Escape Game/Écran de télé (sur la télé sélectionnée)", true)]
        static bool ValidateTvScreen() => Selection.activeGameObject != null;
    }
}
