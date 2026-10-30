// Exact class excerpts from DynamicWin main 9f24d17, UI/Menu/Menus/SettingsMenu.cs.
// CC BY-SA 4.0, Florian Butz; see NOTICE.md. Only namespace imports were separated.
using DynamicWin.UI;
using DynamicWin.UI.UIElements;
using DynamicWin.Utils;
using DynamicWin.Resources;
using SkiaSharp;
namespace DynamicWin.UI.Menu.Menus
{
    public class Checkbox : DWImageButton
    {
        bool isChecked = false;
        public bool IsChecked { get { return isChecked; } set => SetChecked(value); }

        void SetChecked(bool isChecked)
        {
            this.isChecked = isChecked;
            Image.Image = isChecked ? Res.Check : null;
        }

        public Checkbox(UIObject? parent, string buttonText, Vec2 position, Vec2 size, Action clickCallback, UIAlignment alignment = UIAlignment.TopCenter) : base(parent, Res.Check, position, size, clickCallback, alignment)
        {
            var text = new DWText(this, buttonText, new Vec2(15, 0), UIAlignment.MiddleRight);
            text.Color = Theme.TextSecond;
            text.Anchor.X = 0;
            text.TextSize = size.Y / 1.5f;
            AddLocalObject(text);

            SetChecked(false);

            hoverScaleMulti = new Vec2(1.05f, 1f);
            clickScaleMulti = new Vec2(0.975f, 1f);
        }

        public override void OnMouseUp()
        {
            IsChecked = !IsChecked;
            base.OnMouseUp();
        }
    }

    public class MultiSelectionButton : UIObject
    {
        string[] options;
        DWTextButton[] buttons;

        public Action<int> onClick;

        int selectedIndex = 0;
        public int SelectedIndex { get => selectedIndex; set => SetSelected(value); }

        public MultiSelectionButton(UIObject? parent, string[] options, Vec2 position, Vec2 size, UIAlignment alignment = UIAlignment.TopCenter, int maxInOneRow = 4) : base(parent, position, size, alignment)
        {
            this.options = options;
            this.buttons = new DWTextButton[options.Length];

            float xPos = 0;
            float yPos = 0;
            int counter = 0;

            for(int i = 0; i < options.Length; i++)
            {
                var lambdaIndex = i; // Either I'm going insane or I don't understand lambdas, but it seems like only the pointer given in to the OnClick() method. This is why this line is needed!
                var action = () => { OnClick(lambdaIndex); }; // For some it just outputs the length of options if there is no seperate variable for it.

                if (counter >= maxInOneRow)
                {
                    counter = 0;
                    yPos += 35;
                    xPos = 0;
                }

                var btn = new DWTextButton(this, options[i], new Vec2(xPos, yPos), new Vec2(Math.Max(75, options[i].Length >= 11 ? (options[i].Length * 9) : 0), 25), action, UIAlignment.MiddleLeft);
                btn.Text.Color = Theme.TextSecond;
                btn.Anchor.X = 0;
                buttons[i] = btn;
                
                AddLocalObject(btn);

                xPos += btn.Size.X + 15;
                counter++;
            }

            Size.Y = Size.Y + yPos;

            SelectedIndex = 0;
        }

        public override void Draw(SKCanvas canvas)
        {
        }

        void SetSelected(int index)
        {
            selectedIndex = index;

            foreach (var button in buttons)
            {
                button.normalColor = Theme.Secondary.Override(a: 0.9f);
            }

            buttons[index].normalColor = (Theme.Primary * 0.65f).Override(a: 0.75f);
        }

        void OnClick(int index)
        {
            SelectedIndex = index;

            onClick.Invoke(index);
        }
    }
}
