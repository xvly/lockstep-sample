//The MIT License (MIT)

//Copyright (c) 2013 Clinton Brennan

//Permission is hereby granted, free of charge, to any person obtaining a copy
//of this software and associated documentation files (the "Software"), to deal
//in the Software without restriction, including without limitation the rights
//to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//copies of the Software, and to permit persons to whom the Software is
//furnished to do so, subject to the following conditions:

//The above copyright notice and this permission notice shall be included in
//all copies or substantial portions of the Software.

//THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
//THE SOFTWARE.
using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(NetworkView))]
public class LockStepManager : MonoBehaviour {
	
	private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
	
	public static readonly int FirstLockStepTurnID = 0;
	
	public static LockStepManager Instance;
	
	public int LockStepTurnID = FirstLockStepTurnID;
	
	private PendingActions pendingActions;
	private ConfirmedActions confirmedActions;
	
	private Queue<IAction> actionsToSend;
	
	private NetworkView nv;
	private NetworkManager networkManager;
	
	public int numberOfPlayers;
	
	private List<string> readyPlayers;
	private List<string> playersConfirmedImReady;
	
	bool initialized = false;
	
	
	
	// Use this for initialization
	void Start () {
		enabled = false;
		
		Instance = this;
		nv = GetComponent<NetworkView>();
		networkManager = FindObjectOfType(typeof(NetworkManager)) as NetworkManager;
		
		networkManager.OnGameStart += PrepGameStart;
	}
	
	#region GameStart
	public void InitGameStartLists() {
		if(initialized) { return; }
		
		readyPlayers = new List<string>(numberOfPlayers);
		playersConfirmedImReady = new List<string>(numberOfPlayers);
		
		initialized = true;
	}
	public void PrepGameStart() {
		
		log.Debug ("GameStart called. My PlayerID: " + Network.player.ToString());
		LockStepTurnID = FirstLockStepTurnID;
		numberOfPlayers = networkManager.NumberOfPlayers;
		pendingActions = new PendingActions(this);
		confirmedActions = new ConfirmedActions(this);
		actionsToSend = new Queue<IAction>();
		
		InitGameStartLists();
		
		nv.RPC ("ReadyToStart", RPCMode.OthersBuffered, Network.player.ToString());
	}
	
	private void CheckGameStart() {
		if(playersConfirmedImReady == null) {
			log.Debug("WARNING!!! Unexpected null reference during game start. IsInit? " + initialized);
			return;
		}
		//check if all expected players confirmed our gamestart message
		if(playersConfirmedImReady.Count == numberOfPlayers - 1) {
			//check if all expected players sent their gamestart message
			if(readyPlayers.Count == numberOfPlayers - 1) {
				//we are ready to start
				log.Debug("All players are ready to start. Starting Game.");
				
				//we no longer need these lists
				playersConfirmedImReady = null;
				readyPlayers = null;
				
				GameStart ();
			}
		}
	}
	
	private void GameStart() {
		//start the LockStep Turn loop
		//LockStepTurn();
		enabled = true;
	}
	
	[RPC]
	public void ReadyToStart(string playerID) {
		log.Debug("Player " + playerID + " is ready to start the game.");
		
		//make sure initialization has already happened -incase another player sends game start before we are ready to handle it
		InitGameStartLists();;
		
		readyPlayers.Add (playerID);
		
		if(Network.isServer) {
			//don't need an rpc call if we are the server
			ConfirmReadyToStartServer(Network.player.ToString() /*confirmingPlayerID*/, playerID /*confirmedPlayerID*/);
		} else {
			nv.RPC("ConfirmReadyToStartServer", RPCMode.Server, Network.player.ToString() /*confirmingPlayerID*/, playerID /*confirmedPlayerID*/);
		}
		
		//Check if we can start the game
		CheckGameStart();
	}
	
	[RPC]
	public void ConfirmReadyToStartServer(string confirmingPlayerID, string confirmedPlayerID) {
		if(!Network.isServer) { return; } //workaround when multiple players running on same machine
		
		log.Debug("Server Message: Player " + confirmingPlayerID + " is confirming Player " + confirmedPlayerID + " is ready to start the game.");
		
		//validate ID
		if(!networkManager.players.ContainsKey(confirmingPlayerID)) {
			//TODO: error handling
			log.Debug("Server Message: WARNING!!! Unrecognized confirming playerID: " + confirmingPlayerID);
			return;
		}
		if(!networkManager.players.ContainsKey(confirmedPlayerID)) {
			//TODO: error handling
			log.Debug("Server Message: WARNING!!! Unrecognized confirmed playerID: " + confirmingPlayerID);
		}
		
		//relay message to confirmed client
		if(Network.player.ToString().Equals(confirmedPlayerID)) {
			//don't need an rpc call if we are the server
			ConfirmReadyToStart(confirmedPlayerID, confirmingPlayerID);
		} else {
			nv.RPC ("ConfirmReadyToStart", RPCMode.OthersBuffered, confirmedPlayerID, confirmingPlayerID);
		}
		
	}
	
	[RPC]
	public void ConfirmReadyToStart(string confirmedPlayerID, string confirmingPlayerID) {
		if(!Network.player.ToString().Equals(confirmedPlayerID)) { return; }
		
		//log.Debug ("Player " + confirmingPlayerID + " confirmed I am ready to start the game.");
		playersConfirmedImReady.Add (confirmingPlayerID);
		
		//Check if we can start the game
		CheckGameStart();
	}
	#endregion
	
	#region Actions
	public void AddAction(IAction action) {
		log.Debug ("Action Added");
		if(!initialized) {
			log.Debug("Game has not started, action will be ignored.");
			return;
		}
		actionsToSend.Enqueue(action);
	}
	
	private bool LockStepTurn() {
		log.Debug ("LockStepTurnID: " + LockStepTurnID);
		//Check if we can proceed with the next turn
		bool nextTurn = NextTurn();
		if(nextTurn) {
			SendPendingAction ();
			//the first and second lockstep turn will not be ready to process yet
			if(LockStepTurnID >= FirstLockStepTurnID + 3) {
				ProcessActions ();
			}
		}
		//otherwise wait another turn to recieve all input from all players
		
		return nextTurn;
	}
	
	/// <summary>
	/// Check if the conditions are met to proceed to the next turn.
	/// If they are it will make the appropriate updates. Otherwise 
	/// it will return false.
	/// </summary>
	private bool NextTurn() {
//		log.Debug ("Next Turn Check: Current Turn - " + LockStepTurnID);
//		log.Debug ("    priorConfirmedCount - " + confirmedActions.playersConfirmedPriorAction.Count);
//		log.Debug ("    currentConfirmedCount - " + confirmedActions.playersConfirmedCurrentAction.Count);
//		log.Debug ("    allPlayerCurrentActionsCount - " + pendingActions.CurrentActions.Count);
//		log.Debug ("    allPlayerNextActionsCount - " + pendingActions.NextActions.Count);
//		log.Debug ("    allPlayerNextNextActionsCount - " + pendingActions.NextNextActions.Count);
//		log.Debug ("    allPlayerNextNextNextActionsCount - " + pendingActions.NextNextNextActions.Count);
		
		if(confirmedActions.ReadyForNextTurn() && pendingActions.ReadyForNextTurn()) {
			//increment the turn ID
			LockStepTurnID++;
			//move the confirmed actions to next turn
			confirmedActions.NextTurn();
			//move the pending actions to this turn
			pendingActions.NextTurn();
			
			return true;
		}
		
		return false;
	}
	
	private void SendPendingAction() {
		IAction action = null;
		if(actionsToSend.Count > 0) {
			action = actionsToSend.Dequeue();
		}
		
		//if no action for this turn, send the NoAction action
		if(action == null) {
			action = new NoAction();
		}
		//add action to our own list of actions to process
		pendingActions.AddAction(action, Convert.ToInt32(Network.player.ToString()), LockStepTurnID, LockStepTurnID);
		//confirm our own action
		confirmedActions.playersConfirmedCurrentAction.Add (Network.player);
		//send action to all other players
		nv.RPC("RecieveAction", RPCMode.Others, LockStepTurnID, Network.player.ToString(), BinarySerialization.SerializeObjectToByteArray(action));
		
		log.Debug("Sent " + (action.GetType().Name) + " action for turn " + LockStepTurnID);
	}
	
	private void ProcessActions() {
		foreach(IAction action in pendingActions.CurrentActions) {
			action.ProcessAction();
		}
	}
	
	[RPC]
	public void RecieveAction(int lockStepTurn, string playerID, byte[] actionAsBytes) {
		//log.Debug ("Recieved Player " + playerID + "'s action for turn " + lockStepTurn + " on turn " + LockStepTurnID);
		IAction action = BinarySerialization.DeserializeObject<IAction>(actionAsBytes);
		if(action == null) {
			log.Debug ("Sending action failed");
			//TODO: Error handle invalid actions recieve
		} else {
			pendingActions.AddAction(action, Convert.ToInt32(playerID), LockStepTurnID, lockStepTurn);
			
			//send confirmation
			if(Network.isServer) {
				//we don't need an rpc call if we are the server
				ConfirmActionServer (lockStepTurn, Network.player.ToString(), playerID);
			} else {
				nv.RPC ("ConfirmActionServer", RPCMode.Server, lockStepTurn, Network.player.ToString(), playerID);
			}
		}
	}
	
	[RPC]
	public void ConfirmActionServer(int lockStepTurn, string confirmingPlayerID, string confirmedPlayerID) {
		if(!Network.isServer) { return; } //Workaround - if server and client on same machine
		
		//log.Debug("ConfirmActionServer called turn:" + lockStepTurn + " playerID:" + confirmingPlayerID);
		//log.Debug("Sending Confirmation to player " + confirmedPlayerID);
		
		if(Network.player.ToString().Equals(confirmedPlayerID)) {
			//we don't need an RPC call if this is the server
			ConfirmAction(lockStepTurn, confirmingPlayerID);
		} else {
			nv.RPC("ConfirmAction", networkManager.players[confirmedPlayerID], lockStepTurn, confirmingPlayerID);
		}
	}
	
	[RPC]
	public void ConfirmAction(int lockStepTurn, string confirmingPlayerID) {
		NetworkPlayer player = networkManager.players[confirmingPlayerID];
		//log.Debug ("Player " + confirmingPlayerID + " confirmed action for turn " + lockStepTurn + " on turn " + LockStepTurnID);
		if(lockStepTurn == LockStepTurnID) {
			//if current turn, add to the current Turn Confirmation
			confirmedActions.playersConfirmedCurrentAction.Add (player);
		} else if(lockStepTurn == LockStepTurnID -1) {
			//if confirmation for prior turn, add to the prior turn confirmation
			confirmedActions.playersConfirmedPriorAction.Add (player);
		} else {
			//TODO: Error Handling
			log.Debug ("WARNING!!!! Unexpected lockstepID Confirmed : " + lockStepTurn + " from player: " + confirmingPlayerID);
		}
	}
	#endregion

	#region Game Frame
	private float TurnLength = 0.2f; //200 miliseconds
	
	private int GameFramesPerLocksetpTurn = 4;
	
	private int GameFramesPerSecond = 20;
	
	private int GameFrame = 0;
	
	private float AccumilatedTime = 0f;
	
	private float FrameLength = 0.05f; //50 miliseconds
	
	//called once per unity frame
	public void Update() {
		//Basically same logic as FixedUpdate, but we can scale it by adjusting FrameLength
		AccumilatedTime = AccumilatedTime + Time.deltaTime;
		
		//in case the FPS is too slow, we may need to update the game multiple times a frame
		while(AccumilatedTime > FrameLength) {
			GameFrameTurn ();
			AccumilatedTime = AccumilatedTime - FrameLength;
		}
	}
	
	private void GameFrameTurn() {
		//first frame is used to process actions
		if(GameFrame == 0) {
			if(LockStepTurn()) {
				GameFrame++;
			}
		} else {
			//update game
			//TODO: Add custom physics
			//SceneManager.Manager.TwoDPhysics.Update (GameFramesPerSecond);
			
			List<IHasGameFrame> finished = new List<IHasGameFrame>();
			foreach(IHasGameFrame obj in SceneManager.Manager.GameFrameObjects) {
				obj.GameFrameTurn(GameFramesPerSecond);
				if(obj.Finished) {
					finished.Add (obj);
				}
			}
			
			foreach(IHasGameFrame obj in finished) {
				SceneManager.Manager.GameFrameObjects.Remove (obj);
			}
			
			GameFrame++;
			if(GameFrame == GameFramesPerLocksetpTurn) {
				GameFrame = 0;
			}
		}
	}
	#endregion
}
