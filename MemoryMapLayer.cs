using Godot;
using System;

[GlobalClass]
public partial class MemoryMapLayer : TileMapLayer
{

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}
	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
	public void onMemoryChange(Vector2I pos,Godot.Collections.Array<bool> memo)
	{
		// GD.Print("绘制位于:",pos,"的记忆");
		//安全
		if ((!memo[1]) && (!memo[0]))
		{
			SetCell(pos,0,new Vector2I(2,0));
		}
		//周围有怪物
		else if (memo[1])
		{
			SetCell(pos,0,new Vector2I(0,0));
		}
		//周围有洞
		else if (memo[0]){
			SetCell(pos,0,new Vector2I(1,0));
		}
	}
	public void clear()
	{
		foreach(var key in GetUsedCells())
		{
			EraseCell(key);
		}
	}
	public void onMeetWallAt(Vector2I pos)
	{
		SetCell(pos,0,new Vector2I(3,0));
	}
}
