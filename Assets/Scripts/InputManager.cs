using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;
using UnityEngine.UI;

public class InputManager : MonoBehaviour
{
    public GameObject lightGO;
    public GameObject vfxGO;
    public Canvas canvas;

    private Material barGraphMat;
    private Material arrowUpMat;
    private Material arrowDownMat;
    private Material arrowLeftMat;
    private Material arrowRightMat;
    private GameObject textLabelSpace;
    private GameObject textLabelReset;

    private int barUID;
    private int accelUID;
    private int opacityUID;
    private int colorUID;
    private int animateUID;

    private VisualEffect vfx;
    private VisualEffect vfx2;
    private VisualEffect vfx3;
    private VisualEffect vfx4;
    private Light light;
    private Vector2 dir;
    private Vector2 startDir;
    private Vector2 center;
    private float radius;
    private float angle;
    private float rotation;
    private float[,] rotMatrix;
    private Vector2 pos;
    private float intensity; // [0.0, 1.0]
    private float intensity2;
    private float acceleration;

    private Color orange;
    private Color red;

    private float fireTimestamp;
    private float clearTimestamp;
    private bool firedOnce;

    public void Start(){
        center = new Vector2(0.5f, 0.5f);
        radius = 0.5f;
        angle = 0.0f;
        rotMatrix = new float[2,2];
        dir = new Vector2(1.0f, 0.0f);
        startDir = new Vector2(1.0f, 0.0f);
        rotation = 0.0f;
        acceleration = 0.0f;

        intensity = 0.0f;
        intensity2 = 0.0f;
        light = lightGO.GetComponent<Light>();
        vfx = vfxGO.GetComponent<VisualEffect>();
        vfx2 = vfxGO.transform.GetChild(0).gameObject.GetComponent<VisualEffect>();
        vfx3 = vfxGO.transform.GetChild(1).gameObject.GetComponent<VisualEffect>();
        vfx4 = vfxGO.transform.GetChild(2).gameObject.GetComponent<VisualEffect>();

        barGraphMat = canvas.transform.GetChild(0).gameObject.GetComponent<Image>().material;
        textLabelSpace = canvas.transform.GetChild(0).gameObject.transform.GetChild(0).gameObject;
        textLabelSpace.SetActive(true);

        arrowRightMat = canvas.transform.GetChild(1).gameObject.transform.GetChild(0).gameObject.GetComponent<Image>().material;
        arrowLeftMat = canvas.transform.GetChild(2).gameObject.transform.GetChild(0).gameObject.GetComponent<Image>().material;
        arrowDownMat = canvas.transform.GetChild(3).gameObject.transform.GetChild(0).gameObject.GetComponent<Image>().material;
        arrowUpMat = canvas.transform.GetChild(4).gameObject.transform.GetChild(0).gameObject.GetComponent<Image>().material;
        textLabelReset = canvas.transform.GetChild(5).gameObject;
        textLabelReset.SetActive(false);

        barUID = Shader.PropertyToID("_Intensity");
        accelUID = Shader.PropertyToID("_Accel");
        opacityUID = Shader.PropertyToID("_Opacity");
        colorUID = Shader.PropertyToID("_Color");
        animateUID = Shader.PropertyToID("_Animate");

        orange = new Color(0.5f, 0.25f, 0.0f);
        red = new Color(1.0f, 0.0f, 0.0f);

        SetIntensity();
        SetTarget();
        SetLightPos();

        InputManagerUtil.ClearProgress = 0.0f;
        firedOnce = false;
    }

    private Vector2 Rot2D(Vector2 v, float a){
        float rad = (a * Mathf.PI) / 180.0f;
        rotMatrix[0,0] = Mathf.Cos(rad);
        rotMatrix[0,1] = -Mathf.Sin(rad);
        rotMatrix[1,0] = Mathf.Sin(rad);
        rotMatrix[1,1] = Mathf.Cos(rad);
        float x = rotMatrix[0,0] * v.x + rotMatrix[0,1] * v.y;
        float y = rotMatrix[1,0] * v.x + rotMatrix[1,1] * v.y;
        v.x = x;
        v.y = y;
        return v;
    }

    private void SetTarget(){
        float dist = radius * 0.5f;
        InputManagerUtil.Coords.x = center.x + dir.x * dist;
        InputManagerUtil.Coords.y = center.y + dir.y * dist;  

        if (InputManagerUtil.Active) {
            InputManagerUtil.ActiveCoords.x = center.x + dir.x * dist;
            InputManagerUtil.ActiveCoords.y = center.y + dir.y * dist;  
        }
    }

    private void SetLightPos(){
        float dist = radius * InputManagerUtil.WorldScale * 0.5f;
        lightGO.transform.position = new Vector3(dir.x * dist, 2.25f, dir.y * dist);
        vfxGO.transform.position = new Vector3(dir.x * dist, 20.0f, dir.y * dist);
    }

    private void SetIntensity(){
        light.intensity = 2.0f + 69.0f * intensity;
        InputManagerUtil.Coords.z = 0.05f + 0.2f * intensity2;
        InputManagerUtil.ActiveCoords.z = 0.0f + 1.0f * intensity2;
        vfx.SetFloat("Intensity", 0.0f + 1.0f * intensity2);
        vfx2.SetFloat("Intensity", 0.0f + 1.0f * intensity2);
        vfx3.SetFloat("Intensity", 0.0f + 1.0f * intensity2);
        vfx4.SetFloat("Intensity", 0.0f + 1.0f * intensity2);
    }
    
    public void Update(){
        

        if(((Time.time  - clearTimestamp) > 15.0f) &&  (textLabelReset.activeSelf == false) && (firedOnce)){
            clearTimestamp = Time.time;
            textLabelReset.SetActive(true);
        }

        InputManagerUtil.Clear = false;
        bool anyMovement = false;
        if(Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.DownArrow)  || Input.GetKey(KeyCode.UpArrow)){
            anyMovement = true;
            acceleration = Mathf.Min(1.5f, acceleration + Time.deltaTime * .5f);
        } else {
            acceleration = 0.0f;
        }

        
        if(Input.GetKey(KeyCode.Backspace))
        {
            InputManagerUtil.Clear = true;
            InputManagerUtil.ClearProgress = Mathf.Min(1.0f, InputManagerUtil.ClearProgress +  Time.deltaTime);
            if (InputManagerUtil.ClearProgress == 1.0f) {textLabelReset.SetActive(false);}
        }

        if(Input.GetKey(KeyCode.Space))
        {
            InputManagerUtil.Active = true;
            InputManagerUtil.ClearProgress = 0.0f;
            fireTimestamp = Time.time;
            firedOnce = true;
            if(anyMovement){
                intensity = Mathf.Max(0.0f, Mathf.Min(1.0f, intensity + Time.deltaTime * 0.05f));
                intensity2 = Mathf.Max(0.0f, Mathf.Min(1.0f, intensity2 + Time.deltaTime * 0.0f));
            } else {
                intensity = Mathf.Max(0.0f, Mathf.Min(1.0f, intensity + Time.deltaTime * 0.2f));
                intensity2 = Mathf.Max(0.0f, Mathf.Min(1.0f, intensity2 + Time.deltaTime * 0.2f));
            }
            SetIntensity();

        } else {
            InputManagerUtil.Active = false;
            if (intensity > 0.0f) {
                intensity = Mathf.Max(0.0f, Mathf.Min(1.0f, intensity - Time.deltaTime * 0.5f));
                SetIntensity();
            }
            if (intensity2 > 0.0f){
                intensity2 = Mathf.Max(0.0f, Mathf.Min(1.0f, intensity2 - Time.deltaTime * 0.25f));
                SetIntensity();
            }
            
        }

        if(InputManagerUtil.Active = true){
            if(Input.GetKey(KeyCode.RightArrow))
            {
                float rotAmount = Mathf.Lerp(30.0f, 5.0f, radius);
                rotation = (rotation + Time.deltaTime * rotAmount * acceleration) % 100.0f;
                angle = 3.6f * rotation;
                dir = Rot2D(startDir, angle);

                SetTarget();
                SetLightPos();

                anyMovement = true;

                float fill = acceleration / 1.5f;
                arrowRightMat.SetFloat(opacityUID, 1.0f);
                arrowRightMat.SetFloat(accelUID, fill);
            } else {
                arrowRightMat.SetFloat(opacityUID, 0.0f);
                arrowRightMat.SetFloat(accelUID, 0.0f);
            }

            if(Input.GetKey(KeyCode.LeftArrow))
            {
                float rotAmount = Mathf.Lerp(30.0f, 5.0f, radius);
                rotation = (rotation - Time.deltaTime * rotAmount * acceleration) % 100.0f;
                angle = 3.6f * rotation;
                dir = Rot2D(startDir, angle);

                SetTarget();
                SetLightPos();
                
                anyMovement = true;

                float fill = acceleration / 1.5f;
                arrowLeftMat.SetFloat(opacityUID, 1.0f);
                arrowLeftMat.SetFloat(accelUID, fill);
            } else {
                arrowLeftMat.SetFloat(opacityUID, 0.0f);
                arrowLeftMat.SetFloat(accelUID, 0.0f);
            }

            if(Input.GetKey(KeyCode.DownArrow))
            {
                radius = Mathf.Max(0.0f, (radius - Time.deltaTime * 0.2f * (acceleration * 1.5f)));

                SetTarget();
                SetLightPos();

                anyMovement = true;
                float fill = acceleration / 1.5f;
                arrowDownMat.SetFloat(opacityUID, 1.0f);
                arrowDownMat.SetFloat(accelUID, fill);
            } else {
                arrowDownMat.SetFloat(opacityUID, 0.0f);
                arrowDownMat.SetFloat(accelUID, 0.0f);
            }

            if(Input.GetKey(KeyCode.UpArrow))
            {
                radius = Mathf.Min(0.875f, (radius + Time.deltaTime * 0.2f * (acceleration * 1.5f)));

                SetTarget();
                SetLightPos();

                anyMovement = true;
                float fill = acceleration / 1.5f;
                arrowUpMat.SetFloat(opacityUID, 1.0f);
                arrowUpMat.SetFloat(accelUID, fill);
            } else {
                arrowUpMat.SetFloat(opacityUID, 0.0f);
                arrowUpMat.SetFloat(accelUID, 0.0f);
            }
        }

        barGraphMat.SetFloat(barUID, intensity);
        barGraphMat.SetColor(colorUID, Color.Lerp(orange, red, intensity));

        if (intensity == 0.0f){
            textLabelSpace.SetActive(true);
        } else {
            textLabelSpace.SetActive(false);
        }

        if (intensity == 1.0f){
            barGraphMat.SetFloat(animateUID, 1.0f);
        } else{
            barGraphMat.SetFloat(animateUID, 0.0f);
        }


    }
}

public static class InputManagerUtil
{
    public static bool Active = false;
    public static Vector4 Coords;
    public static Vector4 ActiveCoords;
    public static float WorldScale;
    public static bool Clear = false;
    public static float ClearProgress = 0.0f;
}
