﻿using Leap.Unity.Query;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

[ExecuteInEditMode]
public class HorizontalPointLayout : MonoBehaviour, ILocalPositionProvider {

  [Tooltip("The parent transform in whose local space the layout points will be "
         + "organized.")]
  public Transform layoutParent;

  [Tooltip("The Transforms whose positions will be spaced out horizontally by this "
         + "component.")]
  public Transform[] layoutTransforms;

  [Header("Layout Settings")]
  public Alignment alignment = Alignment.Center;
  public enum Alignment { Center }
  public float spacing = 0.10f;

  #region Layout Points Enumerator

  public struct LayoutPointsEnumerator {
    Transform[] transforms;
    bool isLocal;
    int index;

    public LayoutPointsEnumerator(Transform[] layoutTransforms, bool isLocal) {
      this.transforms = layoutTransforms;
      this.index = -1;
      this.isLocal = isLocal;
    }

    public LayoutPointsEnumerator GetEnumerator() { return this; }
    public bool MoveNext() { index += 1; return transforms != null
                                                && index != transforms.Length; }
    public Vector3 Current { get { return isLocal ? transforms[index].localPosition
                                                  : transforms[index].position; } }
  }

  #endregion

  public LayoutPointsEnumerator localLayoutPoints {
    get {
      return new LayoutPointsEnumerator(layoutTransforms, isLocal: true);
    }
  }

  public LayoutPointsEnumerator worldLayoutPoints {
    get {
      return new LayoutPointsEnumerator(layoutTransforms, isLocal: false);
    }
  }

  void Reset() {
    if (layoutParent == null) layoutParent = this.transform;
  }

  void Update() {
    updateLayout();
  }

  private void updateLayout() {
    for (int i = 0; i < layoutTransforms.Length; i++) {
      var point = layoutTransforms[i];

      point.localPosition = localPosForIndex(i);
    }
  }

  private Vector3 localPosForIndex(int index) {
    return localBasePosition + Vector3.right * spacing * index;
  }

  private Vector3 localBasePosition {
    get {
      return -((layoutTransforms.Length - 1) / 2f) * Vector3.right * spacing;
    }
  }

  #region Gizmos

  void OnDrawGizmosSelected() {
    Gizmos.color = Color.Lerp(Color.blue, Color.white, 0.7f);
    bool hasLastPoint = false;
    Vector3 lastPoint = Vector3.zero;
    foreach (var pos in worldLayoutPoints) {
      Gizmos.DrawWireSphere(pos, spacing / 4f);

      if (hasLastPoint) {
        Gizmos.DrawLine(lastPoint, pos);
      }
      lastPoint = pos;
      hasLastPoint = true;
    }
  }

  public Vector3 GetLocalPosition(Transform transform) {
    int idx = layoutTransforms.Query().IndexOf(transform);
    if (idx < 0) {
      Debug.LogError("Cannot return local position for unregistered transform: " + transform);
      return Vector3.zero;
    }

    return localPosForIndex(idx);
  }

  #endregion

}