﻿using System.IO;
using System.Collections.Generic;
using UnityEngine;

public class NoiseCreator : MonoBehaviour {

  public int resolution;
  public TextureFormat format;

  [ContextMenu("Create")]
  void Create() {
    Texture2D tex = new Texture2D(resolution, resolution, format, mipmap: false, linear: true);
    for (int i = 0; i < resolution; i++) {
      for (int j = 0; j < resolution; j++) {
        Vector3 v = Random.onUnitSphere;
        tex.SetPixel(i, j, new Color(v.x, v.y, v.z, 1));
      }
    }
    tex.Apply();

    File.WriteAllBytes("Noise.png", tex.EncodeToPNG());
  }




}
