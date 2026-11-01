from pathlib import Path
from pptx import Presentation
from pptx.util import Inches, Pt
from pptx.dml.color import RGBColor

root = Path(__file__).parent
deck = Presentation()
deck.slide_width = Inches(13.333)
deck.slide_height = Inches(7.5)
for i in range(1, 4):
    slide = deck.slides.add_slide(deck.slide_layouts[6])
    slide.background.fill.solid()
    slide.background.fill.fore_color.rgb = RGBColor(30, 38, 54)
    box = slide.shapes.add_textbox(Inches(1), Inches(2), Inches(11), Inches(2))
    paragraph = box.text_frame.paragraphs[0]
    paragraph.text = f'DesktopSwitcher QA - Slide {i} / 3'
    paragraph.font.size = Pt(40)
    paragraph.font.color.rgb = RGBColor(244, 246, 250)
deck.save(root / 'courseware-test.pptx')
print(root / 'courseware-test.pptx')
