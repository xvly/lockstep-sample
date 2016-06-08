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
using System;
using System.Collections.Generic;
using UnityEngine;

public class ConfirmedActions
{
	
	public List<NetworkPlayer> playersConfirmedCurrentAction;
	public List<NetworkPlayer> playersConfirmedPriorAction;
	
	private LockStepManager lsm;
	
	public ConfirmedActions (LockStepManager lsm)
	{
		this.lsm = lsm;
		playersConfirmedCurrentAction = new List<NetworkPlayer>(lsm.numberOfPlayers);
		playersConfirmedPriorAction = new List<NetworkPlayer>(lsm.numberOfPlayers);
	}
	
	public void NextTurn() {
		//clear prior actions
		playersConfirmedPriorAction.Clear ();
		
		List<NetworkPlayer> swap = playersConfirmedPriorAction;
		
		//last turns actions is now this turns prior actions
		playersConfirmedPriorAction = playersConfirmedCurrentAction;
		
		//set this turns confirmation actions to the empty list
		playersConfirmedCurrentAction = swap;
	}
	
	public bool ReadyForNextTurn() {
		//check that the action that is going to be processed has been confirmed
		if(playersConfirmedPriorAction.Count == lsm.numberOfPlayers) {
			return true;
		}
		//if 2nd turn, check that the 1st turns action has been confirmed
		if(lsm.LockStepTurnID == LockStepManager.FirstLockStepTurnID + 1) {
			return playersConfirmedCurrentAction.Count == lsm.numberOfPlayers;
		}
		//no action has been sent out prior to the first turn
		if(lsm.LockStepTurnID == LockStepManager.FirstLockStepTurnID) {
			return true;
		}
		//if none of the conditions have been met, return false
		return false;
	}
}