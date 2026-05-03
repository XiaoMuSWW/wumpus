using Godot;
using Godot.Collections;
using System;
using System.Collections.Generic;

[GlobalClass]
//绝对不冒险的联机搜索状态机。
// 在线逻辑推演搜索代理。向外提供抽象枚举输出接口，内嵌BFS路径解算
public partial class os_agent : Node
{
	[Export] private ulong seed = 5;
	private RandomNumberGenerator _rng = new RandomNumberGenerator();
	private readonly Vector2I[] directions = { Vector2I.Up, Vector2I.Down, Vector2I.Left, Vector2I.Right };
	
	// 临时封装环境透传依赖
	private mainCharacter currentCharacter;
	private Godot.Collections.Dictionary<Vector2I, Array<bool>> Memory => currentCharacter.memory;
	private Vector2I LogicPosition => currentCharacter.logicPosition;
	private MapManager MapManager => currentCharacter.mapManager;
	
	public override void _Ready()
	{
		base._Ready();
		_rng.Seed = seed;
	}

	/// <summary>
	/// 基于主控提供下发式的分析接口
	/// </summary>
	public mainCharacter.AgentAction GetAction(mainCharacter character)
	{
		currentCharacter = character;

		// 0. 最优先：如果脚下有金矿，立刻挖
		if (HasCurrentGold()) return mainCharacter.AgentAction.Dig;

		var stinks = GetStinkPositions();
		bool hasSafeUnknown = TryFindSafeLocationWithUnknownNeighbors(out var safeTarget);

		// 1. 射箭战术逻辑（手里有箭，且发现了至少一处恶臭）
		if (!character.hadShot && stinks.Count > 0)
		{
			Vector2I? targetWumpus = null;

			// 情景A：遇到两个及以上恶臭时，通过求交集 100% 锁定怪物位置
			if (stinks.Count >= 2)
			{
				var candidates = GetCommonUnknownNeighbors(stinks[0], stinks[1]);
				if (candidates.Count > 0)
				{
					targetWumpus = candidates[0];
				}
			}
			// 情景B：遇到一个恶臭，且走投无路（没有其余安全区可走）时，决定尝试向未知区域发射
			else if (stinks.Count == 1 && !hasSafeUnknown)
			{
				var unknowns = GetUnknownNeighbors(stinks[0]);
				if (unknowns.Count > 0)
				{
					targetWumpus = unknowns[0]; // 盲狙
				}
			}

			// 如果已经锁定了怪物的坐标目标
			if (targetWumpus.HasValue)
			{
				var dir = GetDirectionTowards(LogicPosition, targetWumpus.Value);

				// 检查当前是否站在恶臭点上，并且与怪物贴脸相连
				if (stinks.Contains(LogicPosition) && dir != Vector2I.Zero)
				{
					GD.Print($"Agent: 锁定怪物位置 {targetWumpus.Value}，执行射击！朝向: {dir}");
					return GetActionFromShot(dir);
				}
				else
				{
					// 当前不在合适的射击位置，寻找一个跟怪物相邻的恶臭点作为“狙击点”
					int sniperIndex = stinks.FindIndex(s => GetDirectionTowards(s, targetWumpus.Value) != Vector2I.Zero);
					Vector2I sniperSpot = sniperIndex != -1 ? stinks[sniperIndex] : stinks[0]; // 兜底
					
					if (LogicPosition != sniperSpot)
					{
						var moveTo = GetMoveTowards(sniperSpot);
						GD.Print($"Agent: 怪物在 {targetWumpus.Value}，前往狙击点 {sniperSpot}，当前向 {moveTo} 移动");
						return GetActionFromMove(LogicPosition, moveTo);
					}
				}
			}
		}
		
		// 2. 规避风险：如果脚下是微风，立刻撤回寻找其他有未知邻居的安全点
		if (Memory.ContainsKey(LogicPosition) && Memory[LogicPosition][0])
		{
			if (TryFindSafeLocationWithUnknownNeighbors(out var target, true))
				return GetActionFromMove(LogicPosition, GetMoveTowards(target));
		}
		
		// 3. 正常探索：如果脚下绝对安全，随机探索周围未知方向
		if (IsSafePosition(LogicPosition))
		{
			var unknown = GetUnknownNeighbors(LogicPosition);
			if (unknown.Count > 0)
			{
				var target = unknown[_rng.RandiRange(0, unknown.Count - 1)];
				GD.Print("Agent:尝试迈向未知点:", target);
				return GetActionFromMove(LogicPosition, target);
			}
		}
		
		// 4. 寻路跨越：当前区域已探索完，前往远处的其他安全前线
		if (hasSafeUnknown)
		{
			GD.Print("Agent:移动至有未知邻居的下一个安全点", safeTarget, "\n当前位置:", LogicPosition);
			return GetActionFromMove(LogicPosition, GetMoveTowards(safeTarget));
		}
		
		// 5. 绝境：没有安全点可走，并且没有恶臭指引/箭已用光，彻底穷尽探索
		character.isFinal = true;
		GD.Print("所有可探索路径已经穷尽, OSagent离线");
		return mainCharacter.AgentAction.None;
	}

	public void Feedback(mainCharacter character, bool isDone)
	{
		// 联机搜索为完全启发式推导逻辑，无需通过奖励全局更新网络
	}

	private mainCharacter.AgentAction GetActionFromMove(Vector2I from, Vector2I to)
	{
		var d = to - from;
		if (d == Vector2I.Up) return mainCharacter.AgentAction.Up;
		if (d == Vector2I.Down) return mainCharacter.AgentAction.Down;
		if (d == Vector2I.Left) return mainCharacter.AgentAction.Left;
		if (d == Vector2I.Right) return mainCharacter.AgentAction.Right;
		return mainCharacter.AgentAction.None;
	}

	private mainCharacter.AgentAction GetActionFromShot(Vector2I d)
	{
		if (d == Vector2I.Up) return mainCharacter.AgentAction.ShotUp;
		if (d == Vector2I.Down) return mainCharacter.AgentAction.ShotDown;
		if (d == Vector2I.Left) return mainCharacter.AgentAction.ShotLeft;
		if (d == Vector2I.Right) return mainCharacter.AgentAction.ShotRight;
		return mainCharacter.AgentAction.None;
	}
	
	// 安全点定义：有记忆，没恶臭[1]，没微风[0]，且绝对不是墙[3]
	private bool IsSafePosition(Vector2I pos) => Memory.ContainsKey(pos) && !Memory[pos][0] && !Memory[pos][1] && !Memory[pos][3];
	
	private bool HasCurrentGold() => Memory.ContainsKey(LogicPosition) && Memory[LogicPosition][2];
	
	// 可通行点定义：只要代理曾经走到过（在记忆里）并且不是墙壁，就说明它不会死人，可以用来借道寻路
	private bool IsVisitedAndWalkable(Vector2I pos) => Memory.ContainsKey(pos) && !Memory[pos][3];
	
	private List<Vector2I> GetStinkPositions()
	{
		var scented = new List<Vector2I>();
		foreach (var pair in Memory) {
			if (pair.Value.Count > 1 && pair.Value[1]) scented.Add(pair.Key);
		}
		return scented;
	}

	/// <summary>
	/// 获取两点的相对方向，限制只有两点严格相邻（距离为1）时才返回有效方向
	/// 避免隔山打牛或跨墙射击引发不确定性
	/// </summary>
	private Vector2I GetDirectionTowards(Vector2I from, Vector2I to)
	{
		var delta = to - from;
		if (delta.X == 0 && Math.Abs(delta.Y) == 1) return delta.Y > 0 ? Vector2I.Down : Vector2I.Up;
		if (delta.Y == 0 && Math.Abs(delta.X) == 1) return delta.X > 0 ? Vector2I.Right : Vector2I.Left;
		return Vector2I.Zero; 
	}

	private Vector2I GetMoveTowards(Vector2I target)
	{
		if (target == LogicPosition) return LogicPosition;
		
		var queue = new Queue<Vector2I>();
		var parent = new Godot.Collections.Dictionary<Vector2I, Vector2I>();
		var visited = new HashSet<Vector2I>();
		queue.Enqueue(LogicPosition);
		visited.Add(LogicPosition);

		while (queue.Count > 0)
		{
			var current = queue.Dequeue();
			foreach (var dir in directions)
			{
				var next = current + dir;
				
				// BFS 寻路可以穿过任何我们已经"安全到访过"且不是墙壁的区域（包括有恶臭和微风但没死的地方）
				if (visited.Contains(next) || !IsVisitedAndWalkable(next)) continue;
				
				visited.Add(next);
				parent[next] = current;
				if (next == target)
				{
					var step = next;
					while (parent[step] != LogicPosition) step = parent[step];
					return step;
				}
				queue.Enqueue(next);
			}
		}
		return LogicPosition;
	}
	
	private bool TryFindSafeLocationWithUnknownNeighbors(out Vector2I location, bool excludeCurrent = false)
	{
		foreach (var pos in Memory.Keys)
		{
			if (excludeCurrent && pos == LogicPosition) continue;
			if (!IsSafePosition(pos)) continue; // 必须是完全无害的点（没恶臭、没微风、非墙体）
			
			if (GetUnknownNeighbors(pos).Count > 0) {
				location = pos;
				return true;
			}
		}
		location = Vector2I.Zero;
		return false;
	}
	
	private List<Vector2I> GetCommonUnknownNeighbors(Vector2I a, Vector2I b)
	{
		var first = new HashSet<Vector2I>(GetUnknownNeighbors(a));
		var result = new List<Vector2I>();
		foreach (var pos in GetUnknownNeighbors(b)) {
			if (first.Contains(pos)) result.Add(pos);
		}
		return result;
	}
	
	private List<Vector2I> GetUnknownNeighbors(Vector2I pos)
	{
		var result = new List<Vector2I>();
		foreach (var dir in directions) {
			var next = pos + dir;
			if (!Memory.ContainsKey(next)) result.Add(next);
		}
		return result;
	}
}