using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;

namespace GValley.UI
{
    public class PlayerDialogueMenu : StardewValley.Menus.IClickableMenu
    {
        private readonly StardewValley.Menus.TextBox TextBox;
        private readonly Action<string> OnConfirm;
        private readonly NPC TargetNpc;
        private readonly Texture2D Portrait;

        public PlayerDialogueMenu(IModHelper helper, NPC npc, Texture2D portrait, Action<string> onConfirm)
        {
            this.TargetNpc = npc;
            this.OnConfirm = onConfirm;
            this.Portrait = portrait;

            this.width = 1200;
            this.height = 300;
            Vector2 center = Utility.getTopLeftPositionForCenteringOnScreen(this.width, this.height);
            this.xPositionOnScreen = (int)center.X;
            this.yPositionOnScreen = (int)Game1.uiViewport.Height - this.height - 64;

            this.TextBox = new StardewValley.Menus.TextBox(Game1.content.Load<Texture2D>("LooseSprites\\textBox"), null, Game1.smallFont, Game1.textColor)
            {
                X = this.xPositionOnScreen + (portrait != null ? 300 : 64),
                Y = this.yPositionOnScreen + 100,
                Width = this.width - (portrait != null ? 400 : 128),
                Selected = true,
                limitWidth = false
            };
            this.TextBox.OnEnterPressed += sender => this.Confirm();
        }

        private void Confirm()
        {
            this.OnConfirm(this.TextBox.Text);
            this.exitThisMenu();
        }

        public override void receiveKeyPress(Keys key)
        {
            if (this.TextBox.Selected && key != Keys.Escape)
                return;
            base.receiveKeyPress(key);
        }

        public override void draw(SpriteBatch b)
        {
            StardewValley.Menus.IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60), this.xPositionOnScreen, this.yPositionOnScreen, this.width, this.height, Color.White);
            
            if (this.Portrait != null)
                b.Draw(this.Portrait, new Rectangle(this.xPositionOnScreen + 32, this.yPositionOnScreen + 32, 256, 256), Color.White);

            int labelX = this.xPositionOnScreen + (this.Portrait != null ? 300 : 64);
            Utility.drawTextWithShadow(b, $"Falando com {this.TargetNpc.displayName}:", Game1.dialogueFont, new Vector2(labelX, this.yPositionOnScreen + 40), Game1.textColor);

            this.TextBox.Draw(b);
            base.draw(b);
            this.drawMouse(b);
        }
    }
}