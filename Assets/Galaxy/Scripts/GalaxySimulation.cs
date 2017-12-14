﻿using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using Leap.Unity;
using Leap.Unity.Query;
using Leap.Unity.Attributes;
using Leap.Unity.DevGui;
using Leap.Unity.RuntimeGizmos;

public interface IPropertyMultiplier {
  float multiplier { get; }
}

[DevCategory("General Settings")]
public unsafe class GalaxySimulation : MonoBehaviour {
  public const float TIME_FREEZE_THRESHOLD = 0.05f;
  public const float REFERENCE_FRAMERATE = 90.0f;

  public System.Action OnReset;
  public System.Action OnStep;
  public List<IPropertyMultiplier> TimestepMultipliers = new List<IPropertyMultiplier>();

  //#######################
  //## General Settings ###
  //#######################
  [Header("General Settings")]
  public KeyCode resetKeycode = KeyCode.Space;

  [DevValue]
  public bool simulate = true;

  [DevValue]
  public bool respawnMode = false;

  [DevValue]
  public bool loop = false;

  [Range(0, 10)]
  [DevValue]
  public float loopTime = 10;

  [Range(0, 2)]
  [DevValue]
  [SerializeField]
  private float _timescale = 1;

  [DevValue]
  [SerializeField]
  private bool _limitStepsPerFrame = true;

  public GalaxyRenderer galaxyRenderer;

  //#####################
  //### Star Settings ###
  //#####################
  [Header("Stars"), DevCategory]
  [DevValue("Grav Constant")]
  public float starGravConstant = 5e-05f;

  [DevValue]
  [Range(0, 2)]
  public float minDiscRadius = 0.01f;

  [Range(0, 2)]
  [DevValue]
  public float maxDiscRadius = 1;

  [Range(0, 0.5f)]
  [DevValue]
  public float maxDiscHeight = 1;

  public AnimationCurve radiusDistribution;

  //###########################
  //### Black Hole Settings ###
  //###########################
  [Header("Black Holes"), DevCategory]
  public int blackHoleSubFrames = 10;

  [Range(0, 1)]
  [DevValue("Mass Variance")]
  public float blackHoleMassVariance = 0;

  [Range(0, 1)]
  [DevValue("Mass Affects Radius")]
  public float blackHoleMassAffectsSize = 1;

  [Range(0, 1)]
  [DevValue("Mass Affects Density")]
  public float blackHoleMassAffectsDensity = 1;

  [Range(1, 100)]
  [DevValue("Count")]
  public int blackHoleCount = 3;

  [MinValue(0)]
  [DevValue]
  public float gravConstant = 0.0001f;

  [Range(0, 0.001f)]
  [DevValue]
  public float fuzzValue = 0.0005f;

  [MinValue(0)]
  [DevValue("Start Velocity")]
  public float blackHoleVelocity = 0.1f;

  [Range(0, 1)]
  [DevValue("Direction Variance")]
  public float initialDirVariance = 0;

  [Range(0, 4)]
  [DevValue("Spawn Radius")]
  public float blackHoleSpawnRadius = 0.5f;

  [Range(0, 0.2f)]
  [DevValue("Combine Dist")]
  public float blackHoleCombineDistance = 0.05f;

  //####################
  //### Orbit Trails ###
  //####################
  [Header("Orbit Trails"), DevCategory]
  [SerializeField, DevValue]
  private bool _enableTrails = false;

  [SerializeField]
  private Color _trailColor;
  public Color trailColor {
    get { return _trailColor; }
    set { _trailColor = value; }
  }

  [Range(1, 10000)]
  [SerializeField, DevValue]
  private int _maxTrailLength = 100;

  [Range(2, 10000)]
  [SerializeField, DevValue]
  private int _trailUpdateRate = 2;

  [Range(1, 10000)]
  [SerializeField, DevValue]
  private int _trailShowLength = 500;

  [SerializeField, DevValue]
  private bool _onlyResetWhenComplete = true;

  [SerializeField]
  private Material _trailMaterial;

  [SerializeField]
  private bool _profileTrails = false;

  [SerializeField]
  private double _trailFramerate = 0;

  //##################
  //### References ###
  //##################
  [Header("References")]
  public RenderTexture prevPos;
  public RenderTexture currPos;
  public RenderTexture nextPos;

  private RenderTexture tmpPrev;
  private RenderTexture tmpCurr;
  private RenderTexture tmpNext;
  private List<Drag> drags = new List<Drag>();

  public Material simulateMat;

  public float timescale {
    get {
      float t = _timescale;
      foreach (var multiplier in TimestepMultipliers) {
        t *= multiplier.multiplier;
      }
      return t;
    }
  }

  private int _seed = 0;

  public struct Drag {
    public Matrix4x4 startTransform;
    public Matrix4x4 currTransform;

    public int index;

    public Matrix4x4 deltaTransform {
      get {
        return currTransform * startTransform.inverse;
      }
    }
  }

  [StructLayout(LayoutKind.Sequential, Size = 64)]
  public unsafe struct BlackHole {
    public Vector4 position;
    public Vector4 velocity;
    public float mass;
    public int id;
    public Quaternion rotation;
  }

  [StructLayout(LayoutKind.Sequential)]
  public unsafe struct UniverseState {
    public BlackHole* blackHoles;
    public int numBlackHoles;
    public float time;
    public int frames;
  }

  public class TrailRecord {
    public int startFrame;
    public Deque<Vector3> queue = new Deque<Vector3>(1000);
  }

  public float simulationTime = 0;
  public UniverseState* mainState;
  public UniverseState* prevState;

  private UniverseState* _trailState;
  private bool _trailResetQueued = false;
  private Dictionary<int, TrailRecord> _trails = new Dictionary<int, TrailRecord>();

  private float[] _floatArray = new float[100];
  private Vector4[] _vectorArray = new Vector4[100];
  private Matrix4x4[] _matrixArray = new Matrix4x4[100];

  private Mesh _trailMesh;
  private List<Vector3> _trailVerts = new List<Vector3>();
  private List<int> _trailIndices = new List<int>();
  private MaterialPropertyBlock _trailPropertyBlock;
  private Dictionary<int, int[]> _trailIndexCache = new Dictionary<int, int[]>();

  [DevButton("Reset Sim")]
  public unsafe void ResetSimulation() {
    if (mainState != null) {
      NBodyC.DestroyGalaxy(mainState);
      mainState = null;
    }

    if (prevState != null) {
      NBodyC.DestroyGalaxy(prevState);
      prevState = null;
    }

    if (_trailState != null) {
      NBodyC.DestroyGalaxy(_trailState);
      _trailState = null;
    }

    _trails.Clear();
    _trailMesh.Clear();

    simulationTime = 0;
    mainState = NBodyC.CreateGalaxy(blackHoleCount);
    mainState->time = 0;
    mainState->frames = 0;

    {
      Random.InitState(_seed);
      BlackHole* dst = mainState->blackHoles;

      int nextId = 1;
      for (int i = 0; i < blackHoleCount; i++, dst++) {
        Vector3 position = Random.onUnitSphere * blackHoleSpawnRadius;

        *dst = new BlackHole() {
          position = position,
          velocity = Vector3.Slerp(Vector3.zero - position, Random.onUnitSphere, initialDirVariance).normalized * blackHoleVelocity,
          mass = Random.Range(1 - blackHoleMassVariance, 1 + blackHoleMassVariance),
          id = nextId,
          rotation = Random.rotationUniform
        };

        nextId = nextId << 1;
      }
    }

    Texture2D tex = new Texture2D(512, 1, TextureFormat.RFloat, mipmap: false, linear: true);
    for (int i = 0; i < tex.width; i++) {
      tex.SetPixel(i, 0, new Color(radiusDistribution.Evaluate(i / 512.0f), 0, 0, 0));
    }
    tex.Apply();
    tex.filterMode = FilterMode.Bilinear;
    tex.wrapMode = TextureWrapMode.Clamp;
    simulateMat.SetTexture("_RadiusDistribution", tex);

    updateShaderConstants();

    {
      BlackHole* src = mainState->blackHoles;
      for (int i = 0; i < mainState->numBlackHoles; i++, src++) {
        _vectorArray[i] = src->velocity;
      }
      simulateMat.SetVectorArray("_PlanetVelocities", _vectorArray);
    }

    {
      BlackHole* src = mainState->blackHoles;
      _floatArray.Fill(0);
      for (int i = 0; i < mainState->numBlackHoles; i++, src++) {
        _floatArray[i] = Mathf.Lerp(1, src->mass, blackHoleMassAffectsDensity);
      }
      simulateMat.SetFloatArray("_PlanetDensities", _floatArray);
      simulateMat.SetFloat("_TotalDensity", _floatArray.Query().Fold((a, b) => a + b));
    }

    {
      BlackHole* src = mainState->blackHoles;
      for (int i = 0; i < mainState->numBlackHoles; i++, src++) {
        _floatArray[i] = Mathf.Lerp(1, src->mass, blackHoleMassAffectsSize);
      }
      simulateMat.SetFloatArray("_PlanetSizes", _floatArray);
    }

    GL.LoadPixelMatrix(0, 1, 0, 1);

    prevPos.DiscardContents();
    currPos.DiscardContents();

    RenderBuffer[] buffer = new RenderBuffer[2];
    buffer[0] = prevPos.colorBuffer;
    buffer[1] = currPos.colorBuffer;
    Graphics.SetRenderTarget(buffer, prevPos.depthBuffer);

    simulateMat.SetPass(1);

    GL.Begin(GL.QUADS);
    GL.TexCoord2(0, 0);
    GL.Vertex3(0, 0, 0);
    GL.TexCoord2(1, 0);
    GL.Vertex3(1, 0, 0);
    GL.TexCoord2(1, 1);
    GL.Vertex3(1, 1, 0);
    GL.TexCoord2(0, 1);
    GL.Vertex3(0, 1, 0);
    GL.End();

    prevState = NBodyC.Clone(mainState);
    prevState->time = mainState->time - 1.0f / REFERENCE_FRAMERATE;

    ResetTrails(forceReset: true);

    if (OnReset != null) {
      OnReset();
    }
  }

  [DevButton]
  public void ResetTrails(bool forceReset = false) {
    if (!forceReset && _onlyResetWhenComplete) {
      _trailResetQueued = true;
    } else {
      _trailResetQueued = false;

      if (_trailState != null) {
        NBodyC.DestroyGalaxy(_trailState);
        _trailState = null;
      }

      _trailState = NBodyC.Clone(mainState);

      _trails.Clear();
      unsafe {
        BlackHole* src = mainState->blackHoles;
        for (int i = 0; i < mainState->numBlackHoles; i++, src++) {
          _trails[src->id] = new TrailRecord();
        }
      }
    }
  }

  private IEnumerator Start() {
    NBodyC.SetParams(gravConstant, blackHoleCombineDistance);

    _trailMesh = new Mesh();
    _trailMesh.MarkDynamic();

    _trailPropertyBlock = new MaterialPropertyBlock();

    prevPos.Create();
    currPos.Create();
    nextPos.Create();

    prevPos.DiscardContents();
    currPos.DiscardContents();
    nextPos.DiscardContents();

    ResetSimulation();
    yield return null;
    yield return null;
    ResetSimulation();
  }

  private void OnDisable() {
    if (mainState != null) {
      NBodyC.DestroyGalaxy(mainState);
      mainState = null;
    }

    if (prevState != null) {
      NBodyC.DestroyGalaxy(prevState);
      prevState = null;
    }

    if (_trailState != null) {
      NBodyC.DestroyGalaxy(_trailState);
      _trailState = null;
    }
  }

  private void OnValidate() {
    if (Application.isPlaying) {
      NBodyC.SetParams(gravConstant, blackHoleCombineDistance);
    }
  }

  private unsafe void updateShaderConstants() {
    simulateMat.SetFloat("_MinDiscRadius", minDiscRadius);
    simulateMat.SetFloat("_MaxDiscRadius", maxDiscRadius);
    simulateMat.SetFloat("_MaxDiscHeight", maxDiscHeight);

    {
      BlackHole* src = mainState->blackHoles;
      for (int i = 0; i < mainState->numBlackHoles; i++, src++) {
        Vector4 planet = src->position;
        planet.w = src->mass;
        _vectorArray[i] = planet;
      }
      simulateMat.SetVectorArray("_Planets", _vectorArray);
    }

    {
      BlackHole* src = mainState->blackHoles;
      for (int i = 0; i < mainState->numBlackHoles; i++, src++) {
        _matrixArray[i] = Matrix4x4.Rotate(src->rotation);
      }
      simulateMat.SetMatrixArray("_PlanetRotations", _matrixArray);
    }

    simulateMat.SetInt("_PlanetCount", mainState->numBlackHoles);

    simulateMat.SetFloat("_Force", starGravConstant);
    simulateMat.SetFloat("_FuzzValue", fuzzValue);
  }

  private void Update() {
    if (Input.GetKeyDown(resetKeycode)) {
      ResetSimulation();
    }

    if ((loop && mainState->time > loopTime) || respawnMode) {
      ResetSimulation();
      return;
    }

    Random.InitState(Time.frameCount);
    _seed = Random.Range(int.MinValue, int.MaxValue);

    if (simulate) {
      stepSimulation();
    }

    if (_enableTrails) {
      using (new ProfilerSample("Simulate Trails")) {
        if (_profileTrails) {
          var stopwatch = new System.Diagnostics.Stopwatch();
          stopwatch.Reset();
          stopwatch.Start();
          const int FRAMES_TO_TEST = 1000;
          for (int i = 0; i < FRAMES_TO_TEST; i++) {
            NBodyC.StepGalaxy(_trailState);
          }
          double seconds = stopwatch.ElapsedTicks / (double)System.Diagnostics.Stopwatch.Frequency;
          double framesPerSecond = FRAMES_TO_TEST / seconds;
          _trailFramerate = framesPerSecond;
          Debug.Log("#####: " + _trailFramerate);
        } else {
          int simTime = 0;
          while (_trailState->frames < mainState->frames + _maxTrailLength) {
            NBodyC.StepGalaxy(_trailState);

            unsafe {
              BlackHole* src = _trailState->blackHoles;
              TrailRecord trail;
              for (int j = 0; j < _trailState->numBlackHoles; j++, src++) {
                if (!_trails.TryGetValue(src->id, out trail)) {
                  trail = new TrailRecord() {
                    startFrame = _trailState->frames
                  };
                  _trails[src->id] = trail;
                }

                trail.queue.PushBack(src->position);
              }
            }

            simTime++;
            if (simTime >= _trailUpdateRate) {
              break;
            }
          }
        }
      }

      //Build and display trail mesh
      //but only if it's already reached its max length
      if (_trailState->frames - mainState->frames >= _trailShowLength) {
        using (new ProfilerSample("Display Trails")) {
          _trailVerts.Clear();
          _trailIndices.Clear();

          using (new ProfilerSample("Build Vertex List")) {
            foreach (var pair in _trails) {
              for (int i = 0; i < pair.Value.queue.Count; i++) {
                if (i != 0) {
                  _trailIndices.Add(_trailVerts.Count);
                  _trailIndices.Add(_trailVerts.Count - 1);
                }

                _trailVerts.Add(pair.Value.queue[i]);
              }
            }
          }

          int[] indexArray;
          using (new ProfilerSample("Build Index Array")) {
            int goalLength = Mathf.NextPowerOfTwo(_trailIndices.Count);

            if (!_trailIndexCache.TryGetValue(goalLength, out indexArray)) {
              indexArray = new int[goalLength];
              _trailIndexCache[goalLength] = indexArray;
            }

            for (int i = 0; i < _trailIndices.Count; i++) {
              indexArray[i] = _trailIndices[i];
            }

            for (int i = _trailIndices.Count; i < goalLength; i++) {
              indexArray[i] = 0;
            }
          }

          using (new ProfilerSample("Upload Mesh")) {
            _trailMesh.Clear();
            _trailMesh.SetVertices(_trailVerts);
            _trailMesh.SetIndices(indexArray, MeshTopology.Lines, 0);
          }

          if (_trailResetQueued) {
            ResetTrails(forceReset: true);
          }
        }
      }

      _trailPropertyBlock.SetColor("_Color", _trailColor);
      Graphics.DrawMesh(_trailMesh, galaxyRenderer.displayAnchor.localToWorldMatrix, _trailMaterial, 0, null, 0, _trailPropertyBlock);
    }

    //Render the black holes themselves
    unsafe {
      BlackHole* prevSrc = prevState->blackHoles;
      BlackHole* mainSrc = mainState->blackHoles;
      float fraction = Mathf.InverseLerp(prevState->time, mainState->time, simulationTime);
      for (int j = 0; j < mainState->numBlackHoles; j++, prevSrc++, mainSrc++) {
        Vector3 position = Vector3.Lerp(prevSrc->position, mainSrc->position, fraction);
        galaxyRenderer.DrawBlackHole(position);
      }
    }
  }

  private void LateUpdate() {
    galaxyRenderer.UpdatePositions(currPos, prevPos, nextPos, Mathf.InverseLerp(prevState->time, mainState->time, simulationTime));
  }

  public void BeginDrag(Matrix4x4 startTransform, int blackHoleIndex) {
    if (tmpCurr == null) {
      tmpCurr = RenderTexture.GetTemporary(currPos.width, currPos.height, 0, currPos.format, RenderTextureReadWrite.Linear);
      tmpNext = RenderTexture.GetTemporary(currPos.width, currPos.height, 0, currPos.format, RenderTextureReadWrite.Linear);
      tmpPrev = RenderTexture.GetTemporary(currPos.width, currPos.height, 0, currPos.format, RenderTextureReadWrite.Linear);

      tmpCurr.Create();
      tmpNext.Create();
      tmpPrev.Create();

      Graphics.CopyTexture(currPos, tmpCurr);
      Graphics.CopyTexture(nextPos, tmpNext);
      Graphics.CopyTexture(prevPos, tmpPrev);
    }

    if (drags.Query().Any(d => d.index == blackHoleIndex)) {
      Debug.LogError("Cannot start a second drag with an existing index.");
      return;
    }

    drags.Add(new Drag() {
      startTransform = startTransform,
      currTransform = startTransform,
      index = blackHoleIndex
    });
  }

  public void EndDrag(int blackHoleIndex) {
    int index = drags.Query().IndexOf(g => g.index == blackHoleIndex);
    var drag = drags[index];
    drags.RemoveAt(index);

    applyDrags(drag);

    if (drags.Count == 0) {
      RenderTexture.ReleaseTemporary(tmpCurr);
      RenderTexture.ReleaseTemporary(tmpNext);
      RenderTexture.ReleaseTemporary(tmpPrev);
      tmpCurr = tmpNext = tmpPrev = null;
    } else {
      Graphics.CopyTexture(currPos, tmpCurr);
      Graphics.CopyTexture(nextPos, tmpNext);
      Graphics.CopyTexture(prevPos, tmpPrev);
    }
  }

  public void UpdateDrag(Matrix4x4 currTransform, int blackHoleIndex) {
    int index = drags.Query().IndexOf(g => g.index == blackHoleIndex);
    var drag = drags[index];
    drag.currTransform = currTransform;
    drags[index] = drag;

    applyDrags(drags.ToArray());
  }

  private void applyDrags(params Drag[] drags) {
    simulateMat.SetInt("_NumDrags", drags.Length);

    float[] floatArray = new float[4];
    Matrix4x4[] matArray = new Matrix4x4[4];
    drags.Query().Select(t => (float)t.index / mainState->numBlackHoles).FillArray(floatArray);
    drags.Query().Select(t => t.deltaTransform).FillArray(matArray);

    simulateMat.SetFloatArray("_DragIds", floatArray);
    simulateMat.SetMatrixArray("_DragTransforms", matArray);

    simulateMat.SetTexture("_DragPositions", tmpCurr);
    Graphics.Blit(null, currPos, simulateMat, 2);

    simulateMat.SetTexture("_DragPositions", tmpPrev);
    Graphics.Blit(null, prevPos, simulateMat, 2);

    simulateMat.SetTexture("_DragPositions", tmpNext);
    Graphics.Blit(null, nextPos, simulateMat, 2);
  }

  private void stepSimulation() {
    float deltaTime = timescale / REFERENCE_FRAMERATE;

    simulationTime += deltaTime;
    while (mainState->time < simulationTime) {

      //Simulate black holes
      {
        NBodyC.CopyGalaxy(mainState, prevState);
        NBodyC.StepGalaxy(mainState);
      }

      //Update trails
      {
        foreach (var pair in _trails) {
          if (mainState->frames > pair.Value.startFrame && pair.Value.queue.Count > 0) {
            pair.Value.queue.PopFront();
          }
        }
      }

      //Simulate stars
      {
        updateShaderConstants();

        nextPos.DiscardContents();
        Graphics.Blit(null, nextPos, simulateMat, 0);

        var tmp = prevPos;
        prevPos = currPos;
        currPos = nextPos;
        nextPos = tmp;

        simulateMat.SetTexture("_PrevPositions", prevPos);
        simulateMat.SetTexture("_CurrPositions", currPos);
      }

      if (_limitStepsPerFrame) {
        if (mainState->time < simulationTime) {
          simulationTime = mainState->time;
        }
        break;
      }
    }

    if (OnStep != null) {
      OnStep();
    }
  }

  /*
  private unsafe void stepState(UniverseState state, float deltaTime) {
    float timestepFactor = deltaTime * REFERENCE_FRAMERATE;

    state.time += deltaTime;
    state.frames++;
    float planetDT = 1.0f / blackHoleSubFrames;
    float combinedDT = planetDT * timestepFactor;
    float preStepConstant = gravConstant * planetDT * timestepFactor;
    //float combineDistSqrd = blackHoleCombineDistance * blackHoleCombineDistance;

    for (int stepVar = 0; stepVar < blackHoleSubFrames; stepVar++) {

      //Force accumulation
      {
        BlackHoleMainState* srcA = state.mainState;
        for (int indexA = 0; indexA < state.count; indexA++, srcA++) {

          BlackHoleMainState* srcB = state.mainState + indexA + 1;
          for (int indexB = indexA + 1; indexB < state.count; indexB++, srcB++) {
            float toBX = (*srcB).x - (*srcA).x;
            float toBY = (*srcB).y - (*srcA).y;
            float toBZ = (*srcB).z - (*srcA).z;

            float dist = Mathf.Sqrt(toBX * toBX + toBY * toBY + toBZ * toBZ);
            float forceConst = (*srcA).mass * (*srcB).mass * preStepConstant / (dist * dist * dist);

            float forceX = toBX * forceConst;
            float forceY = toBY * forceConst;
            float forceZ = toBZ * forceConst;

            (*srcA).vx += forceX;
            (*srcA).vy += forceY;
            (*srcA).vz += forceZ;

            (*srcB).vx -= forceX;
            (*srcB).vy -= forceY;
            (*srcB).vz -= forceZ;
          }
        }
      }

      //Position intergration
      {
        BlackHoleMainState* src = state.mainState;
        for (int j = 0; j < state.count; j++, src++) {
          (*src).x += (*src).vx * combinedDT;
          (*src).y += (*src).vy * combinedDT;
          (*src).z += (*src).vz * combinedDT;
        }
      }

      //Black hole combination
      //{
      //  BlackHoleMainState* mainA = state.mainState;
      //  BlackHoleSecondaryState* secondA = state.secondaryState;
      //  for (int indexA = 0; indexA < state.count; indexA++, mainA++, secondA++) {

      //    BlackHoleMainState* mainB = state.mainState + indexA + 1;
      //    BlackHoleSecondaryState* secondB = state.secondaryState + indexA + 1;
      //    for (int indexB = indexA + 1; indexB < state.count; indexB++, mainB++, secondB++) {
      //      float dx = (*mainA).x - (*mainB).x;
      //      float dy = (*mainA).y - (*mainB).y;
      //      float dz = (*mainA).z - (*mainB).z;

      //      float distSqrd = dx * dx + dy * dy + dz * dz;
      //      if (distSqrd <= combineDistSqrd) {
      //        float totalMass = (*mainA).mass + (*mainB).mass;
      //        (*mainA).x = ((*mainA).x * (*mainA).mass + (*mainB).x * (*mainB).mass) / totalMass;
      //        (*mainA).y = ((*mainA).y * (*mainA).mass + (*mainB).y * (*mainB).mass) / totalMass;
      //        (*mainA).z = ((*mainA).z * (*mainA).mass + (*mainB).z * (*mainB).mass) / totalMass;

      //        (*mainA).vx = ((*mainA).vx * (*mainA).mass + (*mainB).vx * (*mainB).mass) / totalMass;
      //        (*mainA).vy = ((*mainA).vy * (*mainA).mass + (*mainB).vy * (*mainB).mass) / totalMass;
      //        (*mainA).vz = ((*mainA).vz * (*mainA).mass + (*mainB).vz * (*mainB).mass) / totalMass;

      //        (*mainA).mass += (*mainB).mass;

      //        state.count--;
      //        *mainB = *(state.mainState + state.count);
      //        *secondB = *(state.secondaryState + state.count);

      //        indexB--;
      //        mainB--;
      //        secondB--;
      //      }
      //    }
      //  }
      //}
    }
  }
  */
}
