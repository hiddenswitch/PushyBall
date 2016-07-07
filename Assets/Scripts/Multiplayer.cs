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
		public GameInput[] lastPlayerInputs = new GameInput[2] { new GameInput (), new GameInput () };

		public override object Clone ()
		{
			return new GameState () {
				ballPosition = this.ballPosition,
				ballVelocity = this.ballVelocity,
				conservation = this.conservation,
				impulseScale = this.impulseScale,
				frames = this.frames,
				lastPlayerInputs = (GameInput[])this.lastPlayerInputs.Clone ()
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
			lastPlayerInputs [0] = new GameInput ();
			lastPlayerInputs [0].Deserialize (readFrom);
			lastPlayerInputs [1] = new GameInput ();
			lastPlayerInputs [1].Deserialize (readFrom);
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
			lastPlayerInputs [0].Serialize (writeTo);
			lastPlayerInputs [1].Serialize (writeTo);
		}

		public GameState ()
		{
		}
	}

	[System.Serializable]
	public class GameInput : HiddenSwitch.Multiplayer.Input
	{
		public float Time;
		public bool MouseDown;
		public Vector2 Position;


		public override object Clone ()
		{
			return new GameInput (Position, Time, MouseDown);
		}

		public override void Deserialize (System.IO.BinaryReader readFrom)
		{
			Time = readFrom.ReadSingle ();
			MouseDown = readFrom.ReadBoolean ();
			Position.x = readFrom.ReadSingle ();
			Position.y = readFrom.ReadSingle ();
		}

		public override bool Equals (object obj)
		{
			var other = obj as GameInput;
			return MouseDown == other.MouseDown
			&& Position == other.Position;
		}

		public override void Serialize (System.IO.BinaryWriter writeTo)
		{
			writeTo.Write (Time);
			writeTo.Write (MouseDown);
			writeTo.Write (Position.x);
			writeTo.Write (Position.y);
		}

		public override int GetHashCode (HiddenSwitch.Multiplayer.Input obj)
		{
			return obj.GetHashCode ();
		}

		public override int GetHashCode ()
		{
			return (Position.GetHashCode () << 16) ^ (Time.GetHashCode () << 1) ^ (MouseDown.GetHashCode ());
		}

		public GameInput ()
		{
		}

		public GameInput (Vector2 position, float time, bool mouseDown)
		{
			Position = position;
			Time = time;
			MouseDown = mouseDown;
		}
	}

	public Adaptive<GameState, GameInput> Adaptive { get; set; }

	#region IAdaptiveDelegate implementation

	public GameObject ball;

	public GameInput runtimeInput;
	public GameState runtimeGameState;

	[Header ("Start Game State")]
	public GameState startGameState;



	public GameInput GetCurrentInput ()
	{
		return new GameInput ((Vector2)UnityEngine.Input.mousePosition, Time.time, UnityEngine.Input.GetMouseButton (0));
	}

	public GameState GetStartState ()
	{
		return (GameState)startGameState.Clone ();
	}

	#endregion

	public System.IO.FileStream file;
	public System.IO.TextWriter writer;

	void Awake ()
	{
		Application.targetFrameRate = -1;
		DontDestroyOnLoad (this.gameObject);
		var log = "client.tsv";
		#if SERVER
		log = "server.tsv";
		#endif
		if (System.IO.File.Exists (log)) {
			System.IO.File.Delete (log);
		}
		file = System.IO.File.OpenWrite (log);
		writer = new System.IO.StreamWriter (file);
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
		// The player with the lower player ID is lastPlayerInputs[0]
		var lowId = inputs.Min (t => t.Key);
		foreach (var kv in inputs.OrderBy(t => t.Key)) {
			var input = kv.Value;
			var index = kv.Key == lowId ? 0 : 1;
			var previousInput = mutableState.lastPlayerInputs [index];
			if (input.MouseDown && previousInput.MouseDown) {
				// Move
				var diff = (input.Position - previousInput.Position).normalized;
				mutableState.ballVelocity += new Vector3 (diff.x, diff.y, 0);
			}
			// update last position
			mutableState.lastPlayerInputs [index] = (GameInput)input.Clone ();
		}
		mutableState.ballPosition += mutableState.ballVelocity;
		mutableState.frames++;
		writer.WriteLine (string.Format ("{0}\t{1}\t{2}\t{3}\t{4}\tID {5}\t{6}\t{7}\t{8}\tID {9}\t{10}\t{11}\t{12}", 
			mutableState.frames,
			mutableState.ballPosition.x,
			mutableState.ballPosition.y, 
			mutableState.ballVelocity.x,
			mutableState.ballVelocity.y,
			inputs [0].Key,
			inputs [0].Value.Position.x,
			inputs [0].Value.Position.y,
			inputs [0].Value.MouseDown,
			inputs [1].Key,
			inputs [1].Value.Position.x,
			inputs [1].Value.Position.y,
			inputs [1].Value.MouseDown
		));
		runtimeGameState = (GameState)mutableState.Clone ();
	}

	void Update ()
	{
		// Render the latest state. Called after the network read because this is a lateupdate
		if (Adaptive.Simulation.LatestState != null) {
			ball.transform.position = Adaptive.Simulation.LatestState.ballPosition;
		}

		runtimeInput = GetCurrentInput ();
	}


	void OnGUI ()
	{
		if (Adaptive.Simulation != null) {
			GUI.Label (new Rect (20, 20, 100, 40), Adaptive.Simulation.ElapsedFrameCount.ToString ());
		}
	}
}
