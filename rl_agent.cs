using Godot;
using Godot.Collections;
using System.Collections.Generic;
using System.Linq;

[GlobalClass]
// 基于标准的 Q-Learning 与环境接口同步设计的 RL Agent，状态由坐标定义，专精单张地图探索
public partial class rl_agent : Node
{
	// Q-Table: 状态字符串 -> 动作价值数组
	private System.Collections.Generic.Dictionary<string, float[]> qTable = new();

	[Export] public float LearningRate = 0.2f;
	[Export] public float DiscountFactor = 0.95f;
	[Export] public float Epsilon = 0.15f; // 探索率

	private const int ActionCount = 9; // 与 AgentAction 中的实际行动枚举数量对应

	private string lastState;
	private int lastAction;

	private float stepReward = 0.0f; // 在物理帧内临时聚拢此Step导致的所有Reward事件
	
	private RandomNumberGenerator rng = new RandomNumberGenerator();

	public override void _Ready()
	{
		rng.Randomize();
	}

	/// <summary>
	/// 提供当前回合 Agent 想做的决策动作
	/// </summary>
	public mainCharacter.AgentAction GetAction(mainCharacter character)
	{
		string currentState = GetStateKey(character);
		
		if (!qTable.ContainsKey(currentState))
			qTable[currentState] = new float[ActionCount];

		int actionIdx;
		if (rng.Randf() < Epsilon)
		{
			actionIdx = rng.RandiRange(0, ActionCount - 1);
		}
		else
		{
			float[] values = qTable[currentState];
			float maxVal = values.Max();
			// 解决初局全零时一直偏向第一个方向的死板表现
			var maxIndices = values.Select((val, idx) => new { val, idx })
								   .Where(x => x.val == maxVal)
								   .Select(x => x.idx).ToList();
			actionIdx = maxIndices[rng.RandiRange(0, maxIndices.Count - 1)];
		}

		lastState = currentState;
		lastAction = actionIdx;
		stepReward = -1.0f; // 每一行动的基础存活惩罚
		
		// 枚举中的动作起点自 index 1 (None = 0) 开始
		return (mainCharacter.AgentAction)(actionIdx + 1);
	}

	/// <summary>
	/// 动作落实且游戏物理判断出存活与否后，由主控发送环境的回执
	/// </summary>
	public void Feedback(mainCharacter character, bool isDone)
	{
		if (string.IsNullOrEmpty(lastState)) return;

		float maxNextQ = 0f;
		if (!isDone)
		{
			string nextState = GetStateKey(character);
			if (!qTable.ContainsKey(nextState))
				qTable[nextState] = new float[ActionCount];
			maxNextQ = qTable[nextState].Max();
		}

		float currentQ = qTable[lastState][lastAction];
		
		// TD-Learning 更新公式: Q(s,a) = Q(s,a) + alpha *[Reward + gamma * max_a' Q(s',a') - Q(s,a)]
		qTable[lastState][lastAction] += LearningRate * (stepReward + DiscountFactor * maxNextQ - currentQ);
	}

	/// <summary>
	/// 将游戏状态转换为状态键
	/// </summary>
	/// <param name="character">角色</param>
	/// <returns></returns> 状态键<summary>
	private string GetStateKey(mainCharacter character)
	{
		Vector2I pos = character.logicPosition;
		string memoryState = "None";
		
		if (character.memory.ContainsKey(pos))
		{
			var m = character.memory[pos];
			// 转化为字符串感知记录[Pit, Stench, Gold, Wall]
			memoryState = $"{(m[0] ? 1 : 0)}{(m[1] ? 1 : 0)}{(m[2] ? 1 : 0)}{(m[3] ? 1 : 0)}";
		}
		// 状态哈希 = 自身坐标 + 感知 + 射击情况
		return $"{pos.X},{pos.Y}|{memoryState}|{character.hadShot}";
	}

	//Godot环境被动事件收集
	public void OnFindNewRegion(Vector2I pos, Array<bool> bools) => stepReward += 10.0f;
	public void OnGetGold(Vector2I pos) => stepReward += 100.0f;
	public void OnIsHeardScream(bool isHeard) => stepReward += isHeard ? 50.0f : -10.0f;
	public void OnSuccess(Vector2I pos) => stepReward += 200.0f;
	public void OnFailed(Vector2I pos) => stepReward -= 200.0f;
}