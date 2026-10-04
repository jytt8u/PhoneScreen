"""Regenerate the checked-in Windows icon with Pillow."""
from pathlib import Path
from PIL import Image, ImageDraw

image = Image.new("RGBA", (256, 256), (0, 0, 0, 0))
draw = ImageDraw.Draw(image)
draw.rounded_rectangle((4, 4, 252, 252), 52, fill="#0e141a")
draw.rounded_rectangle((32, 58, 209, 178), 13, fill="#16252f", outline="#71dfb8", width=9)
draw.line((93, 199, 153, 199), fill="#71dfb8", width=9)
draw.line((123, 179, 123, 197), fill="#71dfb8", width=9)
draw.rounded_rectangle((160, 110, 226, 212), 13, fill="#0e141a", outline="#71dfb8", width=9)
draw.line((182, 192, 203, 192), fill="#71dfb8", width=6)
draw.line((58, 144, 75, 116, 96, 143, 123, 96, 146, 119), fill="#71dfb8", width=8, joint="curve")
image.save(Path(__file__).with_name("app.ico"), sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
