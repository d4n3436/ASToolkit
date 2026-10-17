using ASToolkit.Core.Patching;

namespace ASToolkit.Core.Widescreen;

public static partial class WidescreenInstaller
{
    private static readonly ScriptEdit[] Edits =
    [
        new("Maps/COMMON.LGC", "#define FADE_GAME_TIME        1000", "#define FADE_GAME_TIME        300"),
        new("Maps/COMMON.LGC", "AddCoorX(int unit,int dx)",
            "AddCoorY(int unit,int dy) //menu helper: shift a sprite on Y\n{\n  Action( unit,ACT_CHANGE_COOR,GetX(unit),GetY(unit)+dy,GetZ(unit) );\n}\n\nAddCoorX(int unit,int dx)"),
        new("Maps/COMMON.LGC", "SetScrollType(SCROLL_TO_FLAGMAN_WITH_DIRECTION);",
            "if( ScreenX()<1440 ) SetScrollType(SCROLL_TO_FLAGMAN_WITH_DIRECTION); else SetScrollType(SCROLL_FLAGMAN_IN_CENTER);", 2),
        new(StatebarPath, "  int i,menu;\n  string text;", "  int i,j,menu,itemID,menuChild,itemDir,itemOffsetY;\n  string text;"),
        new(StatebarPath, "    MenuLoad(\"maps\\\\gamebar\"+itoa(ScreenX())+\".men\");", """
                for( i = 0; i < 6; i++ )
                {
                  menu = CreateSprite(747, (ScreenX()-21) - 42*(5-i), ScreenY()+500, 500);
                  AddCoorY(menu, -ScreenY()+30);
                  if( i == 4 )
                  {
                    itemID = 748;
                    itemDir = 64;
                    itemOffsetY = 0;
                  }
                  else
                  {
                    if( i == 5 )
                    {
                      itemID = 727;
                      itemDir = 0;
                      itemOffsetY = -2;
                    }
                    else
                    {
                      itemID = 728+i;
                      itemDir = 96;
                      itemOffsetY = 4;
                    }
                  }
                  menuChild = CreateSprite(itemID, (ScreenX()-21) - 42*(5-i), ScreenY()+500, 501, itemDir);
                  AddCoorY(menuChild, -ScreenY()+32+itemOffsetY);
                }
                for( j = 0; j < 9; j++ )
                {
                  menu = CreateSprite(710, -32, ScreenY()+500, 500, 25+(25*j));
                  AddCoorY(menu, -ScreenY()+70+36*j);
                  menuChild = CreateSprite(745, 5, ScreenY()+500, 501, 15+(15*j));
                  AddCoorY(menuChild, -ScreenY()+79+36*j);
                }
            """),
        new(StatebarPath, "\n    menu = MenuFind(741,999999);//hp,armor", "\n    menu = CreateSprite(741,60,ScreenY()+464, 500);"),
        new(StatebarPath, "    StateBarScores = CreateSprite(3,2,7,0);", "    StateBarScores = CreateSprite(3, 2, ToScreenY(GetY(MenuFind(747,0)),500), 0);")
    ];
}
