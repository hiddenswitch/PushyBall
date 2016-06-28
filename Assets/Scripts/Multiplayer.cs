using UnityEngine;
using System.Collections;
using HiddenSwitch.Multiplayer;
using System.Linq;

public class Multiplayer : MonoBehaviour, IAdaptiveDelegate<Multiplayer.GameState, Multiplayer.GameInput>
{
	[System.Serializable]
	public class GameState : State
	{
		public Vector3 ballPosition = new Vector3 (0, 0.55f, 0);
		public Vector3 ballVelocity;
		public float conservation = 0.9f;
		public float impulseScale = 0.1f;
		public uint frames = 0;

		public override object Clone ()
		{
			return new GameState () {
				ballPosition = this.ballPosition,
				ballVelocity = this.ballVelocity,
				conservation = this.conservation,
				impulseScale = this.impulseScale,
				frames = this.frames
			};
		}

		public override void Deserialize (System.IO.BinaryReader readFrom)
		{
			ballPosition.x = readFrom.ReadSingle ();
			ballPosition.y = readFrom.ReadSingle ();
			ballPosition.z = readFrom.ReadSingle ();
			ballVelocity.x = readFrom.ReadSingle ();
			ballVelocity.y = readFrom.ReadSingle ();
			ballVelocity.z = readFrom.ReadSingle ();
			conservation = readFrom.ReadSingle ();
			impulseScale = readFrom.ReadSingle ();
			frames = readFrom.ReadUInt32 ();
		}

		public override void Serialize (System.IO.BinaryWriter writeTo)
		{
			writeTo.Write (ballPosition.x);
			writeTo.Write (ballPosition.y);
			writeTo.Write (ballPosition.z);
			writeTo.Write (ballVelocity.x);
			writeTo.Write (ballVelocity.y);
			writeTo.Write (ballVelocity.z);
			writeTo.Write (conservation);
			writeTo.Write (impulseScale);
			writeTo.Write (frames);
		}

		public GameState ()
		{
		}
	}

	[System.Serializable]
	public class GameInput : HiddenSwitch.Multiplayer.Input
	{
		public float DeltaTime;

		public Vector2[] DeltaPositions = new Vector2[0];


		public override object Clone ()
		{
			return new GameInput (DeltaPositions, DeltaTime);
		}

		public override void Deserialize (System.IO.BinaryReader readFrom)
		{
			DeltaTime = readFrom.ReadSingle ();
			var length = readFrom.ReadByte ();
			if (length > 0) {
				DeltaPositions = new Vector2[length];
				for (var i = 0; i < length; i++) {
					DeltaPositions [i] = new Vector2 (readFrom.ReadSingle (), readFrom.ReadSingle ());
				}
			}
		}

		public override bool Equals (object obj)
		{
			return false;
//			var other = obj as GameInput;
//			var positionsEqual = true;
//
//			// If there are no positions, return true. We don't have to transfer delta time.
//			if (other.DeltaPositions.Length == 0
//			    && DeltaPositions.Length == 0) {
//				return true;
//			}
//
//			for (var i = 0; i < DeltaPositions.Length; i++) {
//				if (i < other.DeltaPositions.Length) {
//					if (DeltaPositions [i] != other.DeltaPositions [i]) {
//						positionsEqual = false;
//						break;
//					}
//				} else {
//					positionsEqual = false;
//					break;
//				}
//			}
//			return positionsEqual && (DeltaTime == other.DeltaTime);
		}

		public override void Serialize (System.IO.BinaryWriter writeTo)
		{
			writeTo.Write (DeltaTime);
			writeTo.Write ((byte)DeltaPositions.Length);
			if (DeltaPositions.Length > 0) {
				for (var i = 0; i < DeltaPositions.Length; i++) {
					var position = DeltaPositions [i];
					writeTo.Write (position.x);
					writeTo.Write (position.y);
				}
			}
		}

		public override int GetHashCode (HiddenSwitch.Multiplayer.Input obj)
		{
			return obj.GetHashCode ();
		}

		public override int GetHashCode ()
		{
			if (DeltaPositions.Length >= 1) {
				return (DeltaPositions [0].GetHashCode () << 16) ^ (DeltaTime.GetHashCode ());
			} else {
				return 0;
			}
		}

		public GameInput ()
		{
			DeltaPositions = new Vector2[0];
		}

		public GameInput (Vector2[] deltaPositions, float deltaTime)
		{
			DeltaPositions = deltaPositions ?? new Vector2[0];
			DeltaTime = deltaTime;
		}
	}

	public Adaptive<GameState, GameInput> Adaptive { get; set; }

	#region IAdaptiveDelegate implementation

	public float lastInputTime;
	public Vector2[] lastPositions;
	public GameObject ball;

	public GameInput runtimeInput;
	public GameState runtimeGameState;

	[Header ("Start Game State")]
	public GameState startGameState;



	public GameInput GetCurrentInput ()
	{
		Vector2[] positions;
		if (UnityEngine.Input.mousePresent) {
			if (UnityEngine.Input.GetMouseButton (0)) {
				positions = new Vector2[] { UnityEngine.Input.mousePosition };
			} else {
				positions = new Vector2[0];
			}
		} else {
			positions = UnityEngine.Input.touches.Select (t => t.position).ToArray ();
		}

		var deltaPositions = new Vector2[positions.Length];
		for (var i = 0; i < deltaPositions.Length; i++) {
			if (i < lastPositions.Length) {
				deltaPositions [i] = positions [i] - lastPositions [i];
			} else {
				deltaPositions [i] = Vector2.zero;
			}
		}

		var deltaTime = Time.time - lastInputTime;

		// Update last values
		lastPositions = new Vector2[positions.Length];
		for (var i = 0; i < positions.Length; i++) {
			lastPositions [i] = positions [i];
		}
		lastInputTime = Time.time;

		return new GameInput (deltaPositions, deltaTime);
	}

	public GameState GetStartState ()
	{
		return (GameState)startGameState.Clone ();
	}

	#endregion

	void Awake ()
	{
		Application.targetFrameRate = -1;
		DontDestroyOnLoad (this.gameObject);
	}

	// Use this for initialization
	void Start ()
	{
		#if SERVER
		Adaptive = new Adaptive<GameState, GameInput> (gameManager: this, maxPeerDelay: 2, simulationDelay: 1, port: 12500);
		Adaptive.Simulation.InputHandler = HandleSimulationInputHandler;
		Adaptive.Host (GetStartState ());
		#else
		Adaptive = new Adaptive<GameState, GameInput> (gameManager: this, maxPeerDelay: 2, simulationDelay: 1, port: 12501);
		Adaptive.Simulation.InputHandler = HandleSimulationInputHandler;
		Adaptive.Connect ("127.0.0.1", 12500);
		#endif
	}


	void HandleSimulationInputHandler (GameState mutableState, System.Collections.Generic.KeyValuePair<int, GameInput>[] inputs, int frameIndex)
	{
		if (mutableState == null) {
			return;
		}
		mutableState.ballVelocity = Vector3.zero;
		foreach (var kv in inputs) {
			var hasInput = kv.Value.DeltaPositions.Length > 0;
			if (hasInput) {
				var impulse = kv.Value.DeltaPositions [0].normalized;
				mutableState.ballVelocity += new Vector3 (impulse.x, impulse.y, 0);
			}
		}
		mutableState.ballPosition += mutableState.ballVelocity;
		mutableState.frames++;
		runtimeGameState = (GameState)mutableState.Clone ();
	}

	void Update ()
	{
		// Render the latest state. Called after the network read because this is a lateupdate
		if (Adaptive.Simulation.LatestState != null) {
			ball.transform.position = Adaptive.Simulation.LatestState.ballPosition;
		}
	}
}
