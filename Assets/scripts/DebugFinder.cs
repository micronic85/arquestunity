using UnityEngine;
using Oculus.Interaction;

public class DebugFinder : MonoBehaviour
{
    void Start()
    {
        var all = FindObjectsByType<GrabInteractable>(
            FindObjectsInactive.Include, 
            FindObjectsSortMode.None);
            
        foreach (var g in all)
            Debug.Log($"GrabInteractable: {GetPath(g.gameObject)}");
    }

    string GetPath(GameObject obj)
    {
        string path = obj.name;
        var t = obj.transform.parent;
        while (t != null) { path = t.name + "/" + path; t = t.parent; }
        return path;
    }
}