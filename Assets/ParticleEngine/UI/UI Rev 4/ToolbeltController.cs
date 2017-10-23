﻿using Leap.Unity;
using Leap.Unity.Animation;
using Leap.Unity.Attributes;
using Leap.Unity.Interaction;
using Leap.Unity.PhysicalInterfaces;
using System;
using UnityEngine;

using Pose = Leap.Unity.Pose;

public class ToolbeltController : MonoBehaviour {

  [Tooltip("The controller that makes the toolbelt anchor follow the player.")]
  public FollowingController followingController;

  [Tooltip("The target transform to move when animating the position of the toolbelt "
         + "relative to the player. This transform is manipulated in local space.")]
  public Transform toolbeltAnchor;
  
  public InteractionButton openCloseButton;

  [Header("Open / Close <> Appear / Vanish")]

  [ImplementsInterface(typeof(IPropertySwitch))]
  [SerializeField]
  public MonoBehaviour _appearVanishController;
  public IPropertySwitch appearVanishController {
    get { return _appearVanishController as IPropertySwitch; }
  }

  [Header("Open/Close Animation")]
  public State _state = State.Closed;
  public enum State { Closed, Opened }

  [Space]
  public float openCloseTime = 1f;

  //public Pose localClosedPose = Pose.zero;

  [Space]
  public Vector3 localClosedPosition = Vector3.zero;
  public Vector3 localClosedEuler    = Vector3.zero;

  //public Pose localOpenPose   = Pose.zero;

  public Vector3 localOpenPosition = Vector3.zero;
  public Vector3 localOpenEuler    = Vector3.zero;

  public Action OnOpenBegin     = () => { };
  public Action OnOpenComplete  = () => { };
  public Action OnCloseBegin    = () => { };
  public Action OnCloseComplete = () => { };

  private Tween _openCloseTween;
  private float _openCloseTweenTime;
  private Pose _baseLocalTargetPose;

  void Start() {
    openCloseButton.OnPress += onPress;

    createOpenCloseTween();

    _baseLocalTargetPose = toolbeltAnchor.ToLocalPose();

    if (appearVanishController != null) {
      appearVanishController.OffNow();
    }
  }

  private void createOpenCloseTween() {
    _openCloseTween = Tween.Persistent()
                           .Value(0f, 1f, (x) => { _openCloseTweenTime = x; })
                           .OverTime(openCloseTime)
                           .Smooth(SmoothType.Smooth)
                           .OnReachEnd(OnOpenComplete)
                           .OnReachStart(OnCloseComplete);
  }

  void OnDestroy() {
    if (_openCloseTween.isValid) {
      _openCloseTween.Release();
    }
  }

  private void onPress() {
    if (_state == State.Closed) {
      transitionToOpen();
    }
    else if (_state == State.Opened) {
      transitionToClosed();
    }
  }

  private void transitionToOpen() {
    _openCloseTween.Play(Direction.Forward);

    _state = State.Opened;

    if (appearVanishController != null) {
      appearVanishController.On();
    }

    followingController.locked = true;

    OnOpenBegin();
  }

  private void transitionToClosed() {
    _openCloseTween.Play(Direction.Backward);

    _state = State.Closed;

    if (appearVanishController != null) {
      appearVanishController.Off();
    }

    followingController.locked = false;

    OnCloseBegin();
  }

  private float _lastOpenCloseTweenTime = -1f;

  void Update() {
    if (_lastOpenCloseTweenTime != _openCloseTweenTime) {
      var localClosedPose = new Pose(localClosedPosition, Quaternion.Euler(localClosedEuler));
      var localOpenPose   = new Pose(localOpenPosition,   Quaternion.Euler(localOpenEuler));
      var localUpdatePose = Pose.Lerp(localClosedPose, localOpenPose, _openCloseTweenTime);

      toolbeltAnchor.transform.SetLocalPose(_baseLocalTargetPose.Then(localUpdatePose));

      toolbeltAnchor.transform.localPosition = localUpdatePose.position;
      toolbeltAnchor.transform.localRotation = localUpdatePose.rotation;

      _lastOpenCloseTweenTime = _openCloseTweenTime;
    }
  }

}
