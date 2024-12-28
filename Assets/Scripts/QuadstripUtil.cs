using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class QuadstripUtil 
{

    public static shape2D defineShape2D(float width, Vector3 normal)
    {
        vertex[] vertices = new vertex[]
        {   //position--------------------------------, normal-----------------------, uv.x--
            new vertex( new Vector3(-width*0.5f, 0.0f, 0.0f), normal, 0.0f),
            new vertex( new Vector3( 0.0f, 0.0f, 0.0f), normal, 0.5f),
            new vertex( new Vector3( width*0.5f, 0.0f, 0.0f), normal, 1.0f)
        };

        int[] indices = new int[]
        {
            0, 1,
            1, 2
        };

        shape2D definedShape = new shape2D(vertices, indices);
        return definedShape;

    }

    private static float measureCurve(curvePoint[] points)
    {
        float pointAmount = points.Length;
        float distance = 0.0f;
        for (int i = 0; i < pointAmount - 1; i++)
        {
            distance += Vector3.Distance(points[i].position, points[i + 1].position);
        }
        return distance;
    }

    public struct Quadstrip{
        public Vector3[] positions;
        public Vector3[] normals;
        public Vector2[] uvs;
        public int[] indices;
    }
    // generate mesh from points
    public static Quadstrip loft(shape2D originalShape, curvePoint[] points)
    {
        int vertsAmount_originalShape = originalShape.vertices.Length;
        int indicesAmount_originalShape = originalShape.indices.Length;
        int edgeLoopsAmount = points.Length;
        int segmentsAmount = points.Length - 1;

        int vertsAmount = vertsAmount_originalShape * edgeLoopsAmount;
        int triAmount = indicesAmount_originalShape * segmentsAmount;
        int indicesAmount = triAmount * 3;

        int[] indices = new int[indicesAmount];
        Vector3[] positions = new Vector3[vertsAmount];
        Vector3[] normals = new Vector3[vertsAmount];
        Vector2[] uvs = new Vector2[vertsAmount];

        float curveLength = measureCurve(points);
        float distance = 0.0f;

        // for every point in sampled curve
        for (int pointID = 0; pointID <= points.Length - 1; pointID++)
        {
            curvePoint point = points[pointID];

            if (pointID < points.Length - 1)
            {
                distance += Vector3.Distance(point.position, points[pointID + 1].position);
            }
            float uv_y = distance / curveLength;

            // for each vert in original shape
            for (int vertID = 0; vertID <= vertsAmount_originalShape - 1; vertID++)
            {

                vertex vertex = originalShape.vertices[vertID];
                // for pointID = 2, vertID = 1
                // id = 6 + 1 = 7
                int vert_id = pointID * vertsAmount_originalShape + vertID;

                Matrix4x4 deformMatrix = Matrix4x4.TRS(point.position, point.rotation, point.scale);
                positions[vert_id] = deformMatrix.MultiplyPoint3x4(vertex.position);
                normals[vert_id] = deformMatrix.MultiplyVector(vertex.normal);
                uvs[vert_id] = new Vector2(vertex.uv.x, uv_y);
            }
        }

        // for every segment in segmentsAmount
        // calculate indices

        // indices
        // 0-1-2
        // 2-1-3
        // 2-3-4
        // 4-3-5
        // 1-6-3
        // 3-6-7
        // 3-7-8
        // 8-7-9

        int indexID = 0;
        for (int i = 0; i < segmentsAmount; i++)
        {
            int segmentID = i;
            int quadAmount = vertsAmount_originalShape - 1;

            for (int quadID = 0; quadID < quadAmount; quadID++)
            {

                int v0 = originalShape.vertices.Length * segmentID + quadID;
                int v1 = originalShape.vertices.Length * segmentID + quadID + 1;
                int v2 = originalShape.vertices.Length * (segmentID + 1) + quadID;
                int v3 = originalShape.vertices.Length * (segmentID + 1) + quadID + 1;

                //Tri A
                indices[indexID] = v1; indexID++;
                indices[indexID] = v0; indexID++;
                indices[indexID] = v2; indexID++;

                //Tri B
                indices[indexID] = v1; indexID++;
                indices[indexID] = v2; indexID++;
                indices[indexID] = v3; indexID++;
            }
        }

        Quadstrip quadstrip = new Quadstrip();
        /*
        int vertcount = indices.Length;
        Vector3[] vert = new Vector3[vertcount];
        Vector3[] norm = new Vector3[vertcount];
        Vector2[] uv = new Vector2[vertcount];

        for (int i=0; i<indices.Length; i++){
            vert[i] = positions[indices[i]];
            norm[i] = normals[indices[i]];
            uv[i] = uvs[indices[i]];
        }
        quadstrip.positions = vert;
        quadstrip.normals = norm;
        quadstrip.uvs = uv;
         */

        quadstrip.positions = positions;
        quadstrip.uvs = uvs;
        quadstrip.normals = normals;
        quadstrip.indices = indices;

        return quadstrip;
    }

    public struct vertex
    {
        public Vector3 position;
        public Vector3 normal;
        public Vector2 uv;

        public vertex(Vector3 pos, Vector3 norm, float u)
        {
            position = pos;
            normal = norm;
            uv = new Vector2(u, 0.0f);
        }

    }

    public struct shape2D
    {
        public vertex[] vertices;
        public int[] indices;

        public shape2D(vertex[] verts, int[] indices)
        {
            vertices = verts;
            this.indices = indices;
        }
    }

    public struct curvePoint
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;

        public curvePoint(Vector3 pos, Quaternion rot)
        {
            position = pos;
            rotation = rot;
            scale = new Vector3(1.0f, 1.0f, 1.0f);
        }
    }

    // samples a curve, and returns an array of points
    public static curvePoint[] sampleBezier(Vector3[] controlPoints, float segments)
    {
        Vector3 cp0 = controlPoints[0];
        Vector3 cp1 = controlPoints[1];
        Vector3 cp2 = controlPoints[2];

        List<curvePoint> listPoints = new List<curvePoint>();
        float step = 1.0f / segments;

        // for each step, add sampled point along bezier curve
        // add to list of points
        for (float t = 0; t <= 1; t += step)
        {
            Vector3 pos = interpolatePosition(cp0, cp1, cp2, t);
            Quaternion rot = interpolateRotation(cp0, cp1, cp2, t);
            curvePoint point = new curvePoint(pos, rot);
            listPoints.Add(point);
        }
        return listPoints.ToArray();

    }

    public static Vector3 interpolatePosition(Vector3 cp0, Vector3 cp1, Vector3 cp2, float t)
    {
        //Vector3 a = Vector3.Lerp(cp0, cp1, t);
        //Vector3 b = Vector3.Lerp(cp1, cp2, t);
        //return Vector3.Lerp(a, b,t);

        // local space position of interpolated point
        Vector3 interpolatedPosition = (1.0f - t) * (1.0f - t) * cp0 + 2.0f * (1.0f - t) * t * cp1 + t * t * cp2;
        return interpolatedPosition;

    }

    public static Quaternion interpolateRotation(Vector3 cp0, Vector3 cp1, Vector3 cp2, float t)
    {
        // interpolate point´s derivative
        Vector3 firstDerivative = 2.0f * (1.0f - t) * (cp1 - cp0) + 2f * t * (cp2 - cp1);

        // --- Worldpsace Scale and Rotate ---
        // 1. Translate, Rotate and Scale
        // 2. Subtract Translation
        // --- UNCOMMENT FOR WORLD TRANSFORM ---
        //firstDerivative = worldTransform.TransformPoint(firstDerivative) - worldTransform.position;

        Vector3 tangent = firstDerivative.normalized;
        Vector3 binormal = Vector3.Cross(new Vector3(0.0f, 1.0f, 0.0f), tangent);
        Vector3 normal = Vector3.Cross(tangent, binormal);
        Quaternion interpolatedRotation = Quaternion.LookRotation(tangent, normal);
        return interpolatedRotation;

    }
}
