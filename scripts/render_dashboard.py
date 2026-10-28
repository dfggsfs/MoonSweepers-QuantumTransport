"""Render the shared dashboard scene description with sample data for offline QA.

This is a layout preview, not a screenshot of a running Unity game.
"""
from pathlib import Path
import json
import shutil
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
SCALE = 2
for name in ('overview', 'machines', 'rules'):
    data = json.loads((ROOT / f'artifacts/dashboard-{name}.json').read_text(encoding='utf-8'))
    canvas = Image.new('RGB', (data['width'] * SCALE, data['height'] * SCALE), '#0d161f')
    draw = ImageDraw.Draw(canvas)
    for node in data['nodes']:
        x, y, w, h = (node[k] * SCALE for k in ('X', 'Y', 'Width', 'Height'))
        c = node['Color']
        color = (c['R'], c['G'], c['B'])
        if node['Kind'] == 0:
            draw.rounded_rectangle((x, y, x+w-1, y+h-1), radius=0 if h <= 6 else 10*SCALE, fill=color)
        if node['Kind'] == 0:
            continue
        font_path = Path('C:/Windows/Fonts/msyhbd.ttc' if node['Bold'] else 'C:/Windows/Fonts/msyh.ttc')
        font = ImageFont.truetype(str(font_path), node['FontSize'] * SCALE)
        text = node['Text']
        if node['Kind'] == 2:
            style = data['buttonStyles'][node['Id']]
            rgb = lambda c: (c['R'], c['G'], c['B'])
            draw.rounded_rectangle((x,y,x+w-1,y+h-1),radius=10*SCALE,fill=rgb(style['Border']))
            draw.rounded_rectangle((x+SCALE,y+SCALE,x+w-SCALE-1,y+h-SCALE-1),radius=9*SCALE,fill=rgb(style['Fill']))
            text_color = rgb(style['Ink'])
            draw.text((x+w/2, y+h/2), text, font=font, fill=text_color, anchor='mm')
            continue
        lines = []
        for paragraph in text.split('\n'):
            line = ''
            for ch in paragraph:
                if draw.textlength(line + ch, font=font) > w and line:
                    lines.append(line)
                    line = ch
                else:
                    line += ch
            lines.append(line)
        line_height = node['FontSize'] * 1.35 * SCALE
        max_lines = max(1, int(h / line_height))
        if len(lines) > max_lines:
            lines = lines[:max_lines]
            while lines[-1] and draw.textlength(lines[-1]+'…', font=font) > w:
                lines[-1] = lines[-1][:-1]
            lines[-1] += '…'
        for i, line in enumerate(lines):
            draw.text((x, y+i*line_height), line, font=font, fill=color, anchor='lt')
    canvas.resize((data['width'], data['height']), Image.Resampling.LANCZOS).save(ROOT / f'artifacts/dashboard-{name}.png')
    print(f'dashboard-{name}.png: sample-data layout preview')

states = json.loads((ROOT/'artifacts/dashboard-interactions.json').read_text(encoding='utf-8'))
sheet = Image.new('RGB',(1120*SCALE,300*SCALE),'#0d161f')
draw = ImageDraw.Draw(sheet)
font = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',15*SCALE)
title = ImageFont.truetype('C:/Windows/Fonts/msyhbd.ttc',21*SCALE)
draw.text((24*SCALE,18*SCALE),'按钮交互状态 / INTERACTION STATES',font=title,fill='#e8f2f6',anchor='lt')
rgb = lambda c: (c['R'],c['G'],c['B'])
for i,state in enumerate(states['states']):
    x=(24+(i%3)*365)*SCALE; y=(68+(i//3)*108)*SCALE
    draw.text((x,y),state['label'],font=font,fill='#8da3b3',anchor='lt')
    s=state['style']; w=320*SCALE; h=47*SCALE
    draw.rounded_rectangle((x,y+28*SCALE,x+w,y+28*SCALE+h),radius=10*SCALE,fill=rgb(s['Border']))
    draw.rounded_rectangle((x+SCALE,y+29*SCALE,x+w-SCALE,y+27*SCALE+h),radius=9*SCALE,fill=rgb(s['Fill']))
    draw.text((x+w/2,y+28*SCALE+h/2),'量子传送',font=font,fill=rgb(s['Ink']),anchor='mm')
draw.text((24*SCALE,279*SCALE),'示例状态 · 实机另有按压缩放、点击高亮、开合与内容过渡动画',font=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',11*SCALE),fill='#8da3b3',anchor='lt')
sheet.resize((1120,300),Image.Resampling.LANCZOS).save(ROOT/'artifacts/dashboard-interactions.png')
public = ROOT/'docs/images'
public.mkdir(parents=True,exist_ok=True)
for name in ('overview','machines','rules','interactions'):
    shutil.copy2(ROOT/f'artifacts/dashboard-{name}.png',public/f'dashboard-{name}.png')
