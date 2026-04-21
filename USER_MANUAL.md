RobotControl_MVP 사용자 매뉴얼 (WPF MVP + 3D PR#2 포함)
본 문서는 coolmslee/RobotControl_MVP 프로젝트의 사용자(운영자/테스터) 관점 실행/조작 방법을 설명합니다.
기본 MVP(2D 뷰/모션/시뮬/플러그인)는 PR #1이 main에 머지(2026-04-17) 되었고, 3D 기능은 PR #2 브랜치(draft/open) 에 포함될 수 있습니다.

0. 빠른 시작(요약)
RobotControl_MVP.sln 열기 → 시작 프로젝트 Robot.App.Wpf → 실행(F5)
앱에서 Connect
2D 탭에서 MoveLinear/MoveArc 테스트
(PR #2) 3D 탭에서 q1..q6 조작, 충돌 확인, Front/Side/Work PNG 캡처
1. 용어/개념 요약
1.1 6축(고정) 매핑
MVP는 6축을 다음처럼 고정 매핑합니다.

Axis1 = X (mm)
Axis2 = Y (mm)
Axis3 = Z (mm)
Axis4 = Rx (deg)
Axis5 = Ry (deg)
Axis6 = Rz (deg)
본 MVP에서 “로봇 관절(J1~J6)”은 기본 엔진 기준으로는 직접 다루지 않을 수 있으며, 3D(PR#2)에서는 UR5 유사 링크 표시를 위해 별도의 관절 슬라이더(q1..q6)가 존재할 수 있습니다.

2. 설치/준비
2.1 필수 설치
Windows 10/11
Visual Studio 2022 (권장) 또는 .NET 8 SDK + 빌드 환경
.NET 8 SDK
2.2 저장소 내려받기(추천: Git)
(A) 처음 받기
git clone https://github.com/coolmslee/RobotControl_MVP.git
cd RobotControl_MVP
(B) 이미 받은 프로젝트 갱신(main)
cd <로컬폴더>\RobotControl_MVP
git checkout main
git pull
(C) 3D 기능(PR #2) 브랜치 받기(머지 전이라도 확인 가능)
PR #2 브랜치명(예):

copilot/add-wpf-3d-simulation-view
cd <로컬폴더>\RobotControl_MVP
git fetch origin
git checkout copilot/add-wpf-3d-simulation-view
git pull
3. 실행 방법(Visual Studio)
3.1 솔루션 열기/실행
RobotControl_MVP.sln 열기
시작 프로젝트를 Robot.App.Wpf로 설정
실행(F5)
4. 설정 파일 및 드라이버 폴더(중요)
4.1 machine.json 위치
앱 실행 시 다음 위치에 설정을 읽거나 생성합니다.

AppContext.BaseDirectory\machine.json
즉, Visual Studio에서 실행하면 보통:

Robot.App.Wpf\bin\Debug\net8.0-windows\machine.json 같은 출력 폴더 아래에 생깁니다.
4.2 Drivers 폴더(플러그인 드라이버)
앱은 실행 폴더 기준으로 다음을 스캔합니다.

AppContext.BaseDirectory\Drivers\*.dll
기본 MVP는 빌드 후 자동으로 시뮬 드라이버 DLL을 Drivers로 복사해주는 빌드 타깃이 포함되어 있습니다.

5. 기본 MVP(2D + 모션 + 시뮬) 사용법 (main/PR#1)
5.1 화면 구성(개요)
상단 제어:
Connect
MoveLinear
MoveArc3D_3Point
ResetAlarm
(시뮬 드라이버일 때) 안전 토글: E-Stop, DoorOpen 등
입력:
Target Pose: X, Y, Z, Rx, Ry, Rz
Via (Arc용): ViaX, ViaY, ViaZ
Feed(mm/s)
2D 시각화 탭:
Top (XY)
Front (XZ)
Side (YZ)
5.2 Connect (반드시 먼저)
Connect 클릭
내부에서 다음이 수행됩니다.
machine.json 로드(없으면 기본값 생성 후 저장)
Drivers 폴더에서 드라이버 플러그인 로드
machine.json에 지정된 driverId로 장치 생성
실시간 MotionEngine 시작(틱 + UI 갱신 시작)
Connect 실패 시 체크

Drivers\ 폴더에 Robot.Drivers.Sim.dll이 있는지
machine.json에 지정된 driverId가 실제 로드된 드라이버 목록에 있는지
5.3 MoveLinear
Target X/Y/Z/Rx/Ry/Rz 입력
Feed(mm/s) 입력
MoveLinear 클릭
5.4 MoveArc3D_3Point
Via X/Y/Z 입력
Target X/Y/Z/Rx/Ry/Rz 입력
Feed(mm/s) 입력
MoveArc3D_3Point 클릭
주의

Via의 회전(Rx/Ry/Rz)은 사용하지 않을 수 있습니다(위치만 사용).
회전은 start→target 기준으로만 보간될 수 있습니다.
6. 알람/안전 동작
6.1 알람이 발생하는 대표 상황
소프트 리밋(축 제한) 위반
장치(E-Stop) 입력 활성화
알람 상태가 되면 보통:

즉시 정지
현재 모션/큐 클리어
새 모션 명령 거부(ResetAlarm 전까지)
6.2 복구 절차
원인 제거(E-Stop 해제 등)
ResetAlarm 클릭
다시 모션 수행
7. 2D 시각화 해석(Top/Front/Side)
Top(XY): 위에서 본 TCP 궤적
Front(XZ): 정면에서 본 TCP 궤적
Side(YZ): 측면에서 본 TCP 궤적
TCP 점(현재 위치) + 트레일(최근 궤적)이 표시됩니다.
알람 상태일 때 트레일 색이 바뀔 수 있습니다.
8. 3D 기능(PR #2) 사용법 (WPF 내부 3D 시뮬레이션)
이 섹션은 PR #2 브랜치에 포함될 수 있는 기능을 기준으로 설명합니다.

8.1 3D 탭 진입
UI에 3D 탭이 추가됩니다.
내부적으로 HelixViewport3D 기반의 3D 뷰를 사용합니다.
8.2 3D 화면에서 보이는 요소
UR5 유사 형태의 로봇 링크(primitive로 구성)
작업물 박스(500 × 300 × 300 mm)
안전펜스(작업물의 “반대편” 방향에 자동 배치)
8.3 관절 슬라이더(q1..q6)
3D 탭에서 q1..q6 슬라이더로 로봇 자세를 직접 변경할 수 있습니다.
목적:
가시성 확인(어느 자세에서 가려지는지)
대략적인 충돌 여부 확인(링크가 작업물/펜스를 침범하는지)
홍보용 포즈 연출
8.4 작업물 랜덤 배치
작업물 박스 크기: 500×300×300(mm)
랜덤 배치 영역:
X: [300, 900] mm
Y: [-400, 400] mm
Z: 중심 Z=150 mm (즉 바닥이 Z=0에 닿는 배치)
랜덤 모드:

“매 실행 랜덤” 모드
“시드 고정(재현 가능)” 모드(선택 + Seed 입���)
8.5 안전펜스 배치 규칙
작업물의 중심 p=(x,y,0)을 기준으로, 로봇 원점(0,0,0)에서 작업물의 반대편 방향에 펜스를 배치합니다.
펜스는 얇은 박스(plane 근사)로 표현됩니다(폭/높이/두께는 기본값을 사용).
8.6 충돌 표시(간이)
링크를 단순화된 기하(세그먼트+반경 등)로 근사하고,
작업물/펜스를 박스(OBB/AABB)로 근사하여 충돌을 검사합니다.
충돌 시:
충돌 링크가 빨간색으로 하이라이트
상태 텍스트에 Collision: ... 표시
이 충돌은 “MVP 근사”이며, 실제 CAD/정밀 충돌과는 차이가 있을 수 있습니다.

8.7 카메라 프리셋 + PNG 캡처(홍보/리포트용)
카메라 프리셋: Front / Side / Work
각 프리셋에서 PNG 스크린샷 캡처 기능이 제공됩니다.
권장 캡처 시나리오:

정면(Front): 제품 소개/정면 구도
측면(Side): 작업 범위/간섭 검증
작업장면(Work): 위에서 비스듬히 내려다보는 “현장” 느낌 구도
캡처 파일 저장 위치

구현에 따라 다르지만, 일반적으로 앱 실행 폴더 하위 또는 사용자 지정 경로로 저장됩니다.
찾는 방법:
버튼 클릭 시 메시지/상태바 출력 확인
출력 폴더(bin\Debug\net8.0-windows\)에서 *.png 검색
9. 스크린샷(Front/Side/Work) 수집 체크리스트
9.1 사전 준비
3D 브랜치(또는 머지된 main)에서 앱 실행
3D 탭 진입
원하는 로봇 포즈를 q1..q6로 설정
작업물 랜덤/시드 고정 여부 선택(홍보용은 시드 고정 추천)
9.2 캡처 권장 3장(최소)
Front 프리셋 → PNG 캡처
Side 프리셋 → PNG 캡처
Work 프리셋 → PNG 캡처
권장 파일명 예시:

shot_front.png
shot_side.png
shot_work.png
9.3 충돌/가시성 검증용 캡처 팁
충돌이 발생하는 포즈를 일부러 만들고(링크 빨강)
collision_front.png
collision_work.png 같은 이름으로 캡처하면 리포트에 바로 쓰기 좋습니다.
10. Ready for review → merge 체크리스트(PR #2)
PR #2가 draft/open 상태일 때만 필요합니다.

10.1 Ready for review
PR 페이지 열기 (예: /pull/2)
Draft 표시가 있으면 Ready for review 버튼 클릭
10.2 머지 전 확인
Files changed에서 변경사항 검토
Checks가 있다면 통과 확인
This branch has conflicts가 뜨면 conflict 해결 필요
10.3 Merge
PR 하단의 Merge pull request
머지 방식 선택(권장: Squash and merge)
Confirm
10.4 머지 후 확인
main에서 최신으로 갱신 후 실행
앱에 3D 탭이 보이고, Front/Side/Work 캡처가 되면 완료
11. 자주 발생하는 문제 해결(FAQ)
11.1 Connect 했는데 드라이버가 없다고 나옵니다
Robot.App.Wpf 출력 폴더에 Drivers\Robot.Drivers.Sim.dll 존재 여부 확인
빌드 후 자동복사 타깃이 정상 동작했는지 확인(빌드 Output 로그)
11.2 알람이 걸려서 아무것도 안 움직입니다
E-Stop / DoorOpen이 켜져 있는지 확인(시뮬 토글)
소프트리밋을 넘었는지 확인
원인 제거 후 ResetAlarm
11.3 3D 탭이 안 보입니다
현재 브랜치가 main인지 PR #2 브랜치인지 확인
git branch --show-current
PR #2 브랜치로 체크아웃 후 다시 실행
