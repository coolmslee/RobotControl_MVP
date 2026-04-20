# RobotControl_MVP

## 3D Simulation Tab (WPF)

`Robot.App.Wpf` now includes a **3D** tab using **HelixToolkit.Wpf**.

### Features
- UR5-like 6-axis robot link visualization (parametric primitives)
- 6 joint sliders (`q1..q6`, degrees) for immediate articulation
- Workpiece box: `500 x 300 x 300 mm`
- Workpiece random placement:
  - `X: [300, 900] mm`
  - `Y: [-400, 400] mm`
  - `Z(center): 150 mm`
- Randomness mode:
  - default: new random per run
  - optional: fixed seed mode (checkbox + seed input)
- Auto safety fence placement opposite the workpiece:
  - center at `Normalize(-p) * 1200 mm`, `p=(x,y,0)`
  - size `2500 x 50 x 1800 mm`
  - center `Z=900 mm`, facing robot origin
- Collision highlighting (MVP):
  - link capsule approximation vs workpiece/fence boxes
  - colliding links highlighted red
  - collision status text shown in the 3D tab

### Camera Presets and PNG Capture
- Presets: **Front**, **Side**, **Work**
- Use `Capture Front PNG`, `Capture Side PNG`, `Capture Work PNG`
- Images are saved to:
  - `Robot.App.Wpf/bin/<Configuration>/net8.0-windows/Screenshots/`
