using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GenerateSoftNormals : MonoBehaviour
{

    [SerializeField] private List<Transform> origins;
    
    private Vector3[] positions;
    private Vector3[] normals;

    // Start is called before the first frame update
    void Start()
    {
        gameObject.SetActive(true);

        Mesh mesh = gameObject.GetComponent<MeshFilter>().sharedMesh;
        positions = new Vector3[mesh.vertices.Length];
        positions = mesh.vertices;
        normals = new Vector3[positions.Length];

        for(int i = 0; i < positions.Length; i++){
            positions[i] = gameObject.transform.TransformPoint(positions[i]);
        }

        for(int i = 0; i < positions.Length; i++){
            Vector3 closest = GetClosestOrigin(positions[i]);
            Vector3 normal = Vector3.Normalize(positions[i] - closest);
            normals[i] = new Vector3(normal.x, normal.y, normal.z);
        }
        mesh.SetNormals(normals);

        gameObject.SetActive(false); 
    }

    private Vector3 GetClosestOrigin(Vector3 vPos){
        
        int ID = 0;
        float nearestDist = float.MaxValue;
        for(int i = 0; i < origins.Count; i++){
            float dist = Vector3.Distance(vPos, origins[i].position);
            if (dist < nearestDist){
                ID = i;
                nearestDist = dist;
            }
        }
        return origins[ID].position;
    }

}
