﻿using Leap.Unity.Attributes;
using Leap.Unity.DevCommands;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Leap.Unity.Gestures {

  public class DevCommandGestureTrigger : MonoBehaviour {

    [ImplementsInterface(typeof(IGesture))]
    public MonoBehaviour _gesture;
    public IGesture gesture {
      get { return _gesture as IGesture; }
      set { _gesture = value as MonoBehaviour; }
    }

    public DevCommandType devCommand;

    void Reset() {
      if (gesture == null) {
        gesture = GetComponent<IGesture>();
      }
    }

    void Update() {
      if (gesture.wasFinished) {
        DevCommand.Invoke(devCommand);
      }
    }

  }

}
