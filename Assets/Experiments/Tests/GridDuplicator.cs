using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GridDuplicator : MonoBehaviour {
    public Vector3 Steps = Vector3.one;
    public Vector3Int Counts = Vector3Int.one;
    
    void Start() {
        var parent = transform.parent;
        var position = transform.localPosition;
        var rotation = transform.localRotation;
        
        var center = new Vector3((Counts.x-1) * Steps.x, (Counts.y-1) * Steps.y, (Counts.z-1) * Steps.z) * 0.5f;
        for (int z = 0; z < Counts.z; z++) {
            for (int y = 0; y < Counts.y; y++) {
                for (int x = 0; x < Counts.x; x++) {
                    var offset = new Vector3(x * Steps.x, y * Steps.y, z * Steps.z) - center;
                    var instance = Instantiate(gameObject, position + offset, rotation, parent);
                    var duplicator = instance.GetComponent<GridDuplicator>();
                    Destroy(duplicator);
                }
            }
        }
        
        gameObject.SetActive(false);
    }
}
