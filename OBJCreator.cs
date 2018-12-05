using System;
using System.Text;
using System.Collections.Generic;
using TinyJson;

namespace AssetPipeline {

    public struct Vector3 {
        public readonly float x;
        public readonly float y;
        public readonly float z;

        public Vector3(float x, float y, float z) {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public static explicit operator Vector3(Vector2 v2) {
            return new Vector3(v2.x, v2.y, 0f);
        }

        public static Vector3 back => new Vector3(0,0,-1);
    }

    public struct Vector2 {
        public readonly float x;
        public readonly float y;

        public Vector2(float x, float y) {
            this.x = x;
            this.y = y;
        }

        public static explicit operator Vector2(Vector3 v3) {
            return new Vector2(v3.x, v3.y);
        }
    }

    public struct Color {
        public readonly float r;
        public readonly float g;
        public readonly float b;
        public readonly float a;

        public Color(float r, float g, float b, float a) {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }

        public static Color white => new Color(1,1,1,1);
    }

    public class Mesh {
        public string     name;
        public Vector3[]  vertices;
        public Vector3[]  normals;
        public int[]      triangles;
        public Vector2[]  uv;
        public Color[]    colors;
    }

    public static class OBJCreator {

        public static Mesh[] CreateMeshes(string[] atlas) {
            IEnumerable<TPFrame> shapes = GetShapes(atlas);
            List<Mesh> meshes = new List<Mesh>();

            foreach (TPFrame shape in shapes) {
                Mesh mesh = new Mesh {
                    name      = shape.name,
                    vertices  = shape.vertices,
                    uv        = shape.verticesUV,
                    triangles = shape.triangles,
                    normals   = new Vector3[shape.vertices.Length],
                    colors    = new Color[shape.vertices.Length]
                };

                int numVerts = shape.vertices.Length;

                for (int i = 0; i < numVerts; i++) {
                    mesh.normals[i] = Vector3.back;
                }

                for (int i = 0; i < numVerts; i++) {
                    mesh.colors[i] = Color.white;
                }

                meshes.Add(mesh);
            }

            return meshes.ToArray();
        }

        public static string[] CreateOBJs(Mesh[] meshes) {
            string[] objs = new string[meshes.Length];
            for (int i=0; i<meshes.Length; ++i) {
                objs[i] = MeshToString(meshes[i]);
            }

            return objs;
        }

        private static IEnumerable<TPFrame> GetShapes(string[] lines) {
            List<TPFrame> shapes = new List<TPFrame>();
            List<string> lineBuffer = new List<string>();
            List<int[]> arrayBuffer = new List<int[]>();
            bool inFrame = false;
            string lnVertices = "";
            string lnVerticesUV = "";
            string lnTriangles = "";
            Vector2 vertLinePadding = new Vector2(14, 2);
            Vector2 vertUVLinePadding = new Vector2(16, 2);
            Vector2 triLinePadding = new Vector2(15, 1);
            float pxToUnits = 2048f / 21f;
            string shapeName = "";

            // first: search for meta and get the size
            TPSize size = GetSize(lines);

            for (int i = 2; i < lines.Length; i++) {
                string line = lines[i];
                if (line.EndsWith(":", StringComparison.Ordinal)) {
                    shapeName = line.Substring(1, line.Length - 3);
                    shapeName = shapeName.Replace(".png", "");
                    inFrame = true;
                    continue;
                }

                if (!inFrame) {
                    continue;
                }

                if (line == "}," || line == "}},") {
                    inFrame = false;
                    string lastLine = lineBuffer[lineBuffer.Count - 1];
                    lastLine = lastLine.Substring(0, lastLine.Length - 1);
                    lineBuffer.RemoveAt(lineBuffer.Count - 1);
                    lineBuffer.Add(lastLine);
                    lineBuffer.Add("}");
                    TPFrame tpframe = Join(lineBuffer).FromJson<TPFrame>();
                    tpframe.name = shapeName;

                    // vertices
                    ParseArray(line: lnVertices,
                               padding: vertLinePadding,
                               buffer: arrayBuffer);
                    List<Vector3> vertices = new List<Vector3>();
                    for (int j = 0; j < arrayBuffer.Count; j++) {
                        int[] vraw = arrayBuffer[j];
                        float x = vraw[0] - tpframe.pivot.x * tpframe.sourceSize.w;
                        float y = vraw[1] - tpframe.pivot.y * tpframe.sourceSize.h;
                        Vector3 v = new Vector3(x / pxToUnits,
                                                -y / pxToUnits,
                                                0f);
                        vertices.Add(v);
                    }

                    tpframe.vertices = vertices.ToArray();

                    // vertices UV
                    ParseArray(line: lnVerticesUV,
                               padding: vertUVLinePadding,
                               buffer: arrayBuffer);
                    List<Vector2> uvs = new List<Vector2>();
                    for (int j = 0; j < arrayBuffer.Count; j++) {
                        int[] uvRaw = arrayBuffer[j];
                        uvs.Add(new Vector2((float) uvRaw[0] / size.w,
                                            1f - ((float) uvRaw[1] / size.h)));
                    }

                    tpframe.verticesUV = uvs.ToArray();

                    // triangles
                    ParseArray(line: lnTriangles,
                               padding: triLinePadding,
                               buffer: arrayBuffer);
                    List<int> triangles = new List<int>();
                    for (int j = 0; j < arrayBuffer.Count; j++) {
                        int[] traw = arrayBuffer[j];
                        for (int k = 0; k < traw.Length; k++) {
                            triangles.Add(traw[k]);
                        }
                    }

                    tpframe.triangles = triangles.ToArray();

                    shapes.Add(tpframe);
                    lineBuffer.Clear();
                } else {

                    bool addLine = true;
                    if (line.StartsWith("\t\"vertices\": ", StringComparison.Ordinal)) {
                        lnVertices = line;
                        addLine = false;
                    }

                    if (line.StartsWith("\t\"verticesUV\": ", StringComparison.Ordinal)) {
                        lnVerticesUV = line;
                        addLine = false;
                    }

                    if (line.StartsWith("\t\"triangles\": ", StringComparison.Ordinal)) {
                        lnTriangles = line;
                        addLine = false;
                    }

                    if (addLine) {
                        lineBuffer.Add(line);
                    }
                }
            }

            return shapes;
        }

        private static void ParseArray(string line, Vector2 padding, List<int[]> buffer) {
            buffer.Clear();
            // strip beginning
            line = line.Remove(0, (int) padding.x);
            // strip end
            line = line.Remove(line.Length - (int) padding.y);
            line = line.Replace(", ", ";");

            string[] coordsRaw = line.Split(';');
            for (int c = 0; c < coordsRaw.Length; c++) {
                string coordRaw = coordsRaw[c];
                coordRaw = coordRaw.Replace("[", "");
                coordRaw = coordRaw.Replace("]", "");
                string[] coordStr = coordRaw.Split(',');
                int[] coord = new int[coordStr.Length];
                for (int n = 0; n < coord.Length; n++) {
                    coord[n] = int.Parse(coordStr[n]);
                }

                buffer.Add(coord);
            }
        }

        private static string Join(List<string> strings) {
            string str = "";
            for (int i = 0; i < strings.Count; i++) {
                str += strings[i];
            }

            return str;
        }

        private static TPSize GetSize(string[] lines) {
            string size = GetMetaData("size", lines);
            return string.IsNullOrEmpty(size)
                ? null
                : size.FromJson<TPSize>();
        }

        private static string GetMetaData(string label, string[] lines) {
            int metaLine = -1;
            for (int i = lines.Length - 1; i >= 0; i--) {
                if (lines[i] != "\"meta\": {") {
                    continue;
                }

                metaLine = i;
                break;
            }

            string labelStart = "\t\"" + label + "\": ";
            for (int i = metaLine; i < lines.Length; i++) {
                if (!lines[i].StartsWith(labelStart, StringComparison.Ordinal)) {
                    continue;
                }

                string line = lines[i].Replace(labelStart, "");
                return line.Substring(0, line.Length - 1);
            }

            return null;
        }

        private static string MeshToString(Mesh mesh) {
            StringBuilder sb = new StringBuilder();

            sb.Append("g ").Append(mesh.name).Append("\n");
            foreach (Vector3 v in mesh.vertices) {
                // For some reason, the x coordinates needs to be inverted.
                // Probably has to do with OBJs coordinate system
                sb.Append($"v {-v.x} {v.y} {v.z}\n");
            }

            sb.Append("\n");

            foreach (Vector3 n in mesh.normals) {
                // Invert x here too, just like with the vertices
                sb.Append($"vn {-n.x} {n.y} {n.z}\n");
            }

            sb.Append("\n");

            foreach (Vector3 v in mesh.uv) {
                sb.Append($"vt {v.x} {v.y}\n");
            }

            sb.Append("\n");

            for (int i = 0; i < mesh.triangles.Length; i += 3) {
                sb.Append(string.Format("f {1}/{1}/{1} {0}/{0}/{0} {2}/{2}/{2}\n",
                                        mesh.triangles[i] + 1,
                                        mesh.triangles[i + 1] + 1,
                                        mesh.triangles[i + 2] + 1));

            }

            // Support for sub meshes removed for now. Texture Packer doesn't output
            // sub meshes anyway.
//            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++) {
//                sb.Append("\n");
//                // sb.Append("usemtl ").Append(mat.name).Append("\n");
//                // sb.Append("usemap ").Append(mat.name).Append("\n");
//
//                int[] triangles = mesh.GetTriangles(subMesh);
//                for (int i = 0; i < triangles.Length; i += 3) {
//                    sb.Append(string.Format("f {1}/{1}/{1} {0}/{0}/{0} {2}/{2}/{2}\n",
//                                            triangles[i] + 1,
//                                            triangles[i + 1] + 1,
//                                            triangles[i + 2] + 1));
//
//                }
//            }

            return sb.ToString();
        }
    }

    [Serializable]
    public class TPFrame {
        public string       name;
        public TPRect       frame;
        public bool         rotated;
        public bool         trimmed;
        public TPRect       spriteSourceSize;
        public TPSize       sourceSize;
        public Vector2      pivot;
        [NonSerialized]
        public Vector3[]    vertices;
        [NonSerialized]
        public Vector2[]    verticesUV;
        [NonSerialized]
        public int[]        triangles;
    }

    [Serializable]
    public class TPRect {
        public int x;
        public int y;
        public int w;
        public int h;

        public override string ToString() {
            return $"TPRect (x: {x}, y: {y}, w: {w}, h: {h})";
        }
    }

    [Serializable]
    public class TPSize {
        public int w;
        public int h;

        public override string ToString() {
            return $"TPSize (w: {w}, h: {h})";
        }
    }
}
