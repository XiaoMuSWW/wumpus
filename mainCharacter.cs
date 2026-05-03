using Godot;
using Godot.Collections;
using System;
using System.Collections.Generic;

[GlobalClass]
// 实现 感知-记忆-推理 的联机搜索模型，变为标准化的环境交互代理(Step API模式)
public partial class mainCharacter : CharacterBody2D
{
	[Export] public MapManager mapManager;
	[Export] public MemoryMapLayer memoryShower;
	[Export] public os_agent osAgent;  // 联机搜索代理
	[Export] public rl_agent rlAgent;  // 强化学习代理
	[Export] private int stepSize = 64;[Export] private bool Auto = true;
	[Export] private string AgentType = "OS";[Export] private Vector2I middle_bias = new Vector2I(32, 32);[Export] private float actionCooldown = 0.5f;

	private float timeSinceLastAction = 0f;
	private RandomNumberGenerator rng = new RandomNumberGenerator();

	// 动作的离散动作空间
	public enum AgentAction { None = 0, Up, Down, Left, Right, Dig, ShotUp, ShotDown, ShotLeft, ShotRight }

	// 记忆与状态属性
	public Godot.Collections.Dictionary<Vector2I, Array<bool>> memory = new Godot.Collections.Dictionary<Vector2I, Array<bool>>();
	public bool hadShot = false;
	public Vector2I logicPosition;
	public bool isFinal = false; // OS穷尽探索标记
	public bool isSuccess = false; // 成功拿取金矿标记

	// Agent应通过在其ready调用charater.singal+=链接这些信号来更新自身状态
	[Signal] public delegate void shotToEventHandler(Vector2I pos, Vector2I direction);
	[Signal] public delegate void digAtEventHandler(Vector2I pos);
	[Signal] public delegate void isHeardScreamEventHandler(bool isHeard);
	[Signal] public delegate void findNewRegionEventHandler(Vector2I pos, Array<bool> bools);
	[Signal] public delegate void getGoldEventHandler(Vector2I pos);
	[Signal] public delegate void successEventHandler(Vector2I pos);[Signal] public delegate void failedEventHandler(Vector2I pos);

	public override void _Ready()
	{
		rng.Randomize();
		set_position(new Vector2I(0, 0));
		percepte_move(logicPosition);
		shotTo += mapManager.onShotTo;
		digAt += mapManager.onDigAt;
		mapManager.scream += percepte_scream;

		// 自身对成功/失败的处理
		success += onSuccess;
		failed += onFailed;

		// 将奖励信号关联到 RL Agent
		if (rlAgent != null)
		{
			findNewRegion += rlAgent.OnFindNewRegion;
			getGold += rlAgent.OnGetGold;
			isHeardScream += rlAgent.OnIsHeardScream;
			success += rlAgent.OnSuccess;
			failed += rlAgent.OnFailed;
		}

		base._Ready();
	}

	public override void _PhysicsProcess(double delta)
	{
		if (Auto)
		{
			timeSinceLastAction += (float)delta;
			if (timeSinceLastAction < actionCooldown)
				return;
			timeSinceLastAction = 0f;
		}

		// 1. 获取动作决策
		AgentAction action = AgentAction.None;

		if (Auto)
		{
			if (AgentType == "OS" && osAgent != null)
				action = osAgent.GetAction(this);
			else if (AgentType == "RL" && rlAgent != null)
				action = rlAgent.GetAction(this);
		}
		else
		{
			// 接收玩家输入
			if (Input.IsActionJustPressed("W")) action = AgentAction.Up;
			else if (Input.IsActionJustPressed("S")) action = AgentAction.Down;
			else if (Input.IsActionJustPressed("A")) action = AgentAction.Left;
			else if (Input.IsActionJustPressed("D")) action = AgentAction.Right;
			else if (Input.IsActionJustPressed("mouse_right")) action = AgentAction.Dig;
			else if (!hadShot)
			{
				if (Input.IsActionJustPressed("shot_up")) action = AgentAction.ShotUp;
				else if (Input.IsActionJustPressed("shot_down")) action = AgentAction.ShotDown;
				else if (Input.IsActionJustPressed("shot_left")) action = AgentAction.ShotLeft;
				else if (Input.IsActionJustPressed("shot_right")) action = AgentAction.ShotRight;
			}
		}

		if (action == AgentAction.None && !isFinal) return;

		// 2. 环境执行动作并生成影响
		ExecuteAction(action);

		// 3. 最终判断
		bool isDone = false;
		var currGridType = mapManager.GetGridType(logicPosition);
		if (currGridType == MapManager.GridType.Hole || currGridType == MapManager.GridType.Wumpus)
		{
			EmitSignal(SignalName.failed, logicPosition);
			isDone = true;
		}
		else if (isSuccess || isFinal)
		{
			EmitSignal(SignalName.success, logicPosition);
			isDone = true;
		}

		// 4. 将 Step 反馈回执给代理（用于 RL的 Reward 结算或 OS 的局势追踪）
		if (Auto)
		{
			if (AgentType == "OS" && osAgent != null) osAgent.Feedback(this, isDone);
			else if (AgentType == "RL" && rlAgent != null) rlAgent.Feedback(this, isDone);
		}

		// 5. 局后重置
		if (isDone)
		{
			reSetStatu();
		}
	}

	private void ExecuteAction(AgentAction action)
	{
		Vector2I moveTo = logicPosition;
		Vector2I shotDirection = Vector2I.Zero;
		bool isDig = false;

		switch (action)
		{
			case AgentAction.Up: moveTo += Vector2I.Up; break;
			case AgentAction.Down: moveTo += Vector2I.Down; break;
			case AgentAction.Left: moveTo += Vector2I.Left; break;
			case AgentAction.Right: moveTo += Vector2I.Right; break;
			case AgentAction.Dig: isDig = true; break;
			case AgentAction.ShotUp: shotDirection = Vector2I.Up; break;
			case AgentAction.ShotDown: shotDirection = Vector2I.Down; break;
			case AgentAction.ShotLeft: shotDirection = Vector2I.Left; break;
			case AgentAction.ShotRight: shotDirection = Vector2I.Right; break;
		}

		if (moveTo != logicPosition)
		{
			if (mapManager.GetGridType(moveTo) != MapManager.GridType.None)
			{
				set_position(moveTo);
				percepte_move(logicPosition);
			}
			else
			{
				percepte_move(moveTo);
				memoryShower.onMeetWallAt(moveTo);
			}
		}

		if (isDig)
		{
			dig(logicPosition);
			percepte_dig(logicPosition);
		}

		if (shotDirection != Vector2I.Zero && !hadShot)
		{
			hadShot = true;
			shot(shotDirection);
		}
	}
	/// <summary>
	/// 设置位置
	/// </summary>
	/// <param name="pos">逻辑坐标位置（基于棋盘网格的坐标）</param>
	/// <remarks>
	/// 此方法将逻辑坐标转换为实际像素位置，同时更改角色的两类坐标
	/// </remarks>
	public void set_position(Vector2I pos)
	{
		logicPosition = pos;
		Position = pos * stepSize + middle_bias;
	}
	/// <summary>
	/// 向某处射箭
	/// </summary>
	/// <param name="direction">箭头方向</param>
	/// <remarks>
	/// 其隐含了当前坐标logicPostion，事实上它发射了shotTo信号
	/// </remarks>
	public void shot(Vector2I direction)
	{
		EmitSignal(SignalName.shotTo, logicPosition, direction);
	}
	/// <summary>
	/// 挖掘动作
	/// </summary>
	/// <param name="pos">被挖掘的位置</param>
	public void dig(Vector2I pos)
	{
		EmitSignal(SignalName.digAt, pos);
		if (mapManager.GetGridType(pos) == MapManager.GridType.Gold) {
			GD.Print("得到金矿在:", pos);
		}
	}
	/// <summary>
	/// 通过移动产生感知
	/// </summary>
	/// <param name="pos">感知目标</param>
	public void percepte_move(Vector2I pos)
	{
		if (memory.ContainsKey(pos)) return;

		Array<bool> bools = [false, false, false, false];
		MapManager.GridType gridType = mapManager.GetGridType(pos);
		if (gridType == MapManager.GridType.Gold) bools[2] = true;
		else if (gridType == MapManager.GridType.None) bools[3] = true;

		Array<Vector2I> around =[Vector2I.Up, Vector2I.Down, Vector2I.Left, Vector2I.Right];
		foreach (Vector2I i in around)
		{
			Vector2I preceptePos = pos + i;
			if (memory.ContainsKey(preceptePos)) continue;
			
			if (mapManager.GetGridType(preceptePos) == MapManager.GridType.Hole) bools[0] = true;
			else if (mapManager.GetGridType(preceptePos) == MapManager.GridType.Wumpus) bools[1] = true;
		}

		if (memory.TryAdd(pos, bools)) {
			EmitSignal(SignalName.findNewRegion, pos, bools);
			memoryShower.onMemoryChange(pos, bools);
		}
	}
	/// <summary>
	/// 通过挖掘产生的感知
	/// </summary>
	/// <param name="pos">感知目标位置</param>
	public void percepte_dig(Vector2I pos)
	{
		if (!memory.ContainsKey(pos)) return; 
		if (memory[pos][2])
		{
			GD.Print("挖掘成功");
			EmitSignal(SignalName.getGold, pos);
			isSuccess = true; // 动作引起的结果更新标记
		}
		memory[pos][2] = false;
	}
	/// <summary>
	/// 通过尖叫声产生的感知
	/// </summary>
	/// <param name="isScream">是否真听见了尖叫</param>
	public void percepte_scream(bool isScream)
	{
		var scentedPositions = new List<Vector2I>();
		foreach (var pair in memory) {
			if (pair.Value.Count > 1 && pair.Value[1]) scentedPositions.Add(pair.Key);
		}

		var removePositions = new HashSet<Vector2I>(scentedPositions);
		if (!isScream)
		{
			EmitSignal(SignalName.isHeardScream, false);
			var around = new Vector2I[] { Vector2I.Up, Vector2I.Down, Vector2I.Left, Vector2I.Right };
			foreach (var pos in scentedPositions) {
				foreach (var dir in around) removePositions.Add(pos + dir);
			}
			foreach (var pos in removePositions) {
				if (memory.Remove(pos)) memoryShower.EraseCell(pos);
			}
		}
		else
		{
			EmitSignal(SignalName.isHeardScream, true);
			foreach (var pos in scentedPositions) {
				if (memory.Remove(pos)) {
					memoryShower.EraseCell(pos);
					percepte_move(pos);
				}
			}
		}
	}
	/// <summary>
	/// 检查是否无路可走，即到达最终状态
	/// </summary>
	/// <returns>表示是否到达最终状态</returns>
	private bool CheckIsFinal()
	{
		bool hasFrontier = false;
		bool hasSmell = false;

		foreach (var pair in memory)
		{
			Vector2I pos = pair.Key;
			var m = pair.Value;

			if (m[1]) hasSmell = true;
			if (m[0] || m[1]) continue;

			Vector2I[] dirs = { Vector2I.Up, Vector2I.Down, Vector2I.Left, Vector2I.Right };
			foreach (var d in dirs)
			{
				Vector2I next = pos + d;
				if (!memory.ContainsKey(next)) {
					hasFrontier = true;
					break;
				}
			}
			if (hasFrontier) break;
		}

		if (hasFrontier) return false;
		if (hasSmell && !hadShot) return false;
		return true;
	}

	/// <summary>
	/// 重置状态为初始状态
	/// </summary>
	private void reSetStatu()
	{
		memory.Clear();
		memoryShower.clear();
		hadShot = false;
		isSuccess = false;
		isFinal = false;
		mapManager.reGenerate();
		
		set_position(new Vector2I(0, 0));
		percepte_move(logicPosition);
	}
	
	/// <summary>
	/// 当胜利时触发的槽
	/// </summary>
	/// <param name="pos">用于配合信号，实际上在这个类中无作用</param>
	private void onSuccess(Vector2I pos) => GD.Print("Success Event Fired,成功");
	
	/// <summary>
	/// 当失败时触发的槽
	/// </summary>
	/// <param name="pos">用于配合信号，实际上在这个类中无作用</param>
	private void onFailed(Vector2I pos) => GD.Print("Failed Event Fired,失败");
}