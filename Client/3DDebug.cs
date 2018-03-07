using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkMultiPlayer
{
    public class LineRendererDebug
    {
        //Instance
        private GameObject gameObject;
        private LineRenderer lineRenderer;
        public LineRendererDebug(Color lineColour)
        {
            //Texture
            Texture2D newTex = new Texture2D(1, 1);
            newTex.SetPixel(0, 0, lineColour);
            newTex.Apply();
            //Gameobject setup
            gameObject = new GameObject();
            lineRenderer = gameObject.AddComponent<LineRenderer>();
            //Linerenderer setup
            lineRenderer.startWidth = 0.1f;
            lineRenderer.endWidth = 0.1f;
            lineRenderer.positionCount = 2;
            lineRenderer.startColor = lineColour;
            lineRenderer.endColor = lineColour;
            lineRenderer.material = new Material(Shader.Find("KSP/UnlitColor"));
            lineRenderer.material.mainTexture = newTex;
        }
        public void UpdatePosition(Vector3 startPos, Vector3 endPos)
        {
            lineRenderer.SetPosition(0, startPos);
            lineRenderer.SetPosition(1, endPos);
        }
        public void Destroy()
        {
            GameObject.Destroy(gameObject);
        }
    }

    public class QuaternionRendererDebug
    {
        LineRendererDebug axis;
        LineRendererDebug reference0;
        LineRendererDebug theta;
        public QuaternionRendererDebug(Color lineColour)
        {
            axis = new LineRendererDebug(lineColour);
            reference0 = new LineRendererDebug(lineColour);
            theta = new LineRendererDebug(lineColour);
        }

        public void UpdateRotation(Vector3 startPos, Quaternion referenceTransform, Quaternion inputQuat)
        {
            Vector3 vec3 = new Vector3(3f, 3f, 3f);
            Vector3 vec15 = new Vector3(1.5f, 1.5f, 1.5f);
            Vector3 vec05 = new Vector3(0.5f, 0.5f, 0.5f);
            Vector3 vec025 = new Vector3(0.25f, 0.25f, 0.25f);
            float inputTheta;
            Vector3 inputAxis;
            inputQuat.ToAngleAxis(out inputTheta, out inputAxis);
            //Rotate so unrotated is up.
            Vector3 upVector = FlightGlobals.upAxis;
            //Theta stuff
            Vector3 thetaStart = startPos + (Vector3.Scale(referenceTransform * inputAxis, vec15));
            Vector3 refDirection = Vector3.up;
            float thetaX = Mathf.Sin(inputTheta * Mathf.Deg2Rad);
            float thetaY = Mathf.Cos(inputTheta * Mathf.Deg2Rad);
            Vector3 thetaDirection = new Vector3(thetaX, thetaY, 0f);


            axis.UpdatePosition(startPos, startPos + Vector3.Scale(referenceTransform * inputAxis, vec3));
            reference0.UpdatePosition(thetaStart, thetaStart + Vector3.Scale(referenceTransform * refDirection, vec025));
            theta.UpdatePosition(thetaStart, thetaStart + Vector3.Scale(referenceTransform * thetaDirection, vec05));
        }

        public void Destroy()
        {
            axis.Destroy();
            axis = null;
            reference0.Destroy();
            reference0 = null;
            theta.Destroy();
            theta = null;
        }
    }

    public class PointRendererDebug
    {
        LineRendererDebug xAxis;
        LineRendererDebug yAxis;
        LineRendererDebug zAxis;
        public PointRendererDebug(Color lineColour)
        {
            xAxis = new LineRendererDebug(lineColour);
            yAxis = new LineRendererDebug(lineColour);
            zAxis = new LineRendererDebug(lineColour);
        }

        public void UpdatePosition(Vector3 centrePos, Quaternion referenceTransform)
        {
            Vector3 xOffset = new Vector3(0.25f, 0f, 0f);
            Vector3 yOffset = new Vector3(0f, 0.25f, 0f);
            Vector3 zOffset = new Vector3(0f, 0f, 0.25f);
            xAxis.UpdatePosition(centrePos - (referenceTransform * xOffset), centrePos + (referenceTransform * xOffset));
            yAxis.UpdatePosition(centrePos - (referenceTransform * yOffset), centrePos + (referenceTransform * yOffset));
            zAxis.UpdatePosition(centrePos - (referenceTransform * zOffset), centrePos + (referenceTransform * zOffset));
        }

        public void Destroy()
        {
            xAxis.Destroy();
            xAxis = null;
            yAxis.Destroy();
            yAxis = null;
            zAxis.Destroy();
            zAxis = null;
        }
    }
}

