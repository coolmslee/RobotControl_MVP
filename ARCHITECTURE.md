RobotControl_MVP 코드/아키텍처 설명 (PR#2 3D 포함)
본 문서는 coolmslee/RobotControl_MVP의 코드 구조/책임 분리/데이터 흐름을 설명합니다.
핵심 목표는 Core(엔진/로직)와 Driver(UI/Sim) 분리, 플러그인 기반 확장, 실시간 제어 루프입니다.

1. 전체 구성 요약
1.1 솔루션 구성(4 프로젝트)
Robot.Abstractions
공용 모델/계약(interfaces, DTO)
Robot.Core
실시간 모션 엔진, 설정 로드, 드라이버 로딩, (PR#2) 3D 씬 로직/기구학/충돌 근사
Robot.Drivers.Sim
시뮬레이션 장치(가짜 로봇) 구현. setpoint를 받아 실제값을 따라가게 적분
Robot.App.Wpf
WPF UI (연결/명령/2D 뷰/3D 뷰)
1.2 레이어 의존성(방향)
Robot.Core → Robot.Abstractions
Robot.Drivers.Sim → Robot.Abstractions
Robot.App.Wpf → Robot.Core + Robot.Abstractions
금지

Core가 Sim이나 WPF를 참조하는 구조(Core→UI/Driver 직접 의존)는 피합니다.
2. 도메인 모델(Abstractions)
2.1 6축 고정 모델
MVP는 축을 6개로 고정하고, Axis1..Axis6을 X/Y/Z/Rx/Ry/Rz로 매핑합니다.

장점: MVP 단계에서 기구학/드라이버/GUI를 단순화
단점: 실제 “관절각(J1..J6)”과는 다른 개념일 수 있음
PR#2의 3D 로봇은 “관절각” 시각화를 위해 별도의 q1..q6를 도입할 수 있음
2.2 핵심 인터페이스(개념적)
IRobotDevice
Core가 제어할 대상 장치(실물 또는 시뮬)
IRealtimeTickable
장치가 Core의 실시간 루프에 동기화되어 tick마다 내부 상태를 갱신할 수 있는 훅
IWritableSafetyIo
시뮬레이션 장치에서 안전 입력(EStop 등)을 UI로부터 토글하기 위한 확장 인터페이스
IDriverFactory
플러그인 드라이버를 생성하는 팩토리(DriverId 기반)
3. Core: 실시간 모션 엔진
3.1 MotionEngine 책임
별도 스레드(고우선순위)에서 realtime tick loop 실행
모션 명령 큐를 받아 궤적을 실행하고, 매 tick마다 setpoint를 장치에 전달
Safety(장치 입력) 감시 및 알람/정지 처리
soft limit 위반 시 즉시 정지 + 알람 래치 + 큐/궤적 클리어
UI 업데이트를 위해 일정 주기(~30Hz)로 현재 포즈 publish
3.2 Move 명령 개요
MoveLinear:
start pose → target pose
위치는 선형 보간, 회전은 축별 최단각 보간(구현 정책)
MoveArc3D_3Point:
start = current pose
via/target의 XYZ로 원호를 구성
회전은 start→target 보간(일반적으로 via 회전 무시 정책)
3.3 Tick에서의 장치 갱신(IRealtimeTickable)
장치가 IRealtimeTickable을 구현하면 Core는 매 tick마다:

device.Tick(deltaTimeSeconds) 호출
→ 시뮬 드라이버가 속도/가속 제한 등 물리 갱신을 수행하도록 지원
4. Core: 설정(machine.json) 및 부트스트랩
4.1 machine.json
위치: AppContext.BaseDirectory\machine.json
Connect 시 LoadOrCreateDefault로 로드/생성
포함 가능 항목(개념):
tickMs
axis soft limits
devices 목록(driverId, params)
4.2 DriverPluginLoader(플러그인 로딩)
AppContext.BaseDirectory\Drivers\*.dll을 스캔
각 DLL에서 IDriverFactory 구현 타입을 찾아 인스턴스 생성
machine.json에서 요구하는 driverId와 매칭되는 팩토리를 사용해 장치 생성
5. Sim Driver: Robot.Drivers.Sim
5.1 역할
실제 하드웨어가 없어도 Core/WPF를 검증할 수 있도록 하는 가상 장치
Core가 보내는 setpoint를 받고, tick마다 속도/가속 제한을 적용해 실제값을 점진적으로 추종
5.2 Safety I/O
IWritableSafetyIo를 제공하여 UI에서 EStop/DoorOpen을 토글 가능
EStop이 켜지면 Core의 알람/정지 로직이 동작
6. WPF App: Robot.App.Wpf
6.1 Connect 파이프라인(UI 이벤트 흐름)
사용자가 Connect 클릭
Core의 Config 로드/기본 생성
DriverPluginLoader로 Drivers 폴더 스캔
device 생성
MotionEngine 생성/시작
UI는 엔진의 pose update 이벤트를 구독하여 뷰 갱신
6.2 2D 시각화(Top/Front/Side)
Canvas에 TCP 점 + 트레일(최근 10초, 포인트 제한) 렌더
알람 상태에 따라 색상 변경
6.3 빌드 후 드라이버 배포(자동 복사)
WPF 프로젝트 빌드 후, Sim 드라이버 산출물을 Drivers/로 복사하는 MSBuild 타깃
목적: 실행 폴더에 플러그인 DLL을 자동으로 갖추어, 바로 로딩 가능하게 함
7. (PR #2) 3D 시뮬레이션 아키텍처
목표: WPF에서 3D를 렌더링하되, 수학/충돌/배치 규칙은 Core에 두고 UI는 “그리기/조작”에 집중합니다.

7.1 구성요소(개념)
Core:
UR5 유사 로봇 모델(primitive 치수/관절 범위)
FK(Forward Kinematics): q1..q6 → 링크별 Transform 계산
씬 배치:
작업물 박스 랜덤 배치(X[300..900], Y[-400..400], Zc=150)
펜스 자동 배치(작업물의 반대편)
충돌(근사):
링크(세그먼트+반경/캡슐 근사)
작업물/펜스(박스 근사)
WPF:
HelixViewport3D로 렌더링
q1..q6 슬라이더로 입력
카메라 프리셋(Front/Side/Work)
PNG 캡처 기능
Core가 준 “충돌 링크 목록”을 받아 링크 색상을 빨강으로 변경하고 상태 텍스트 표시
7.2 “실사풍”에 대한 현실적 한계/전략
WPF/Helix는 실시간 “그럴듯한 3D”는 가능하지만,
PBR/글로벌 일루네이션/진짜 실사급 렌더는 한계가 큼
홍보용 품질이 더 필요해지면:
동일 포즈/씬 데이터를 외부 렌더러(예: Blender)로 export하여 오프라인 렌더를 추가하는 확장 전략을 권장
8. 데이터 흐름(요약)
8.1 기본 제어 흐름(2D/���션)
UI(명령) → Core(MotionEngine) → Device(IRobotDevice)
Device(현재값/Safety) → Core(알람/상태) → UI(표시/트레일)

8.2 3D 흐름(PR #2)
UI(q1..q6, 랜덤/시드 옵션) → Core(FK/배치/충돌 계산) → UI(3D 렌더/하이라이트/캡처)

9. PR #2 Ready for review → merge (운영 체크리스트)
9.1 목적
PR #2의 3D 기능을 main에 머지하여, 모든 사용자가 main만 받아도 3D 탭을 사용 가능하게 함
9.2 절차
PR 페이지 열기(예: /pull/2)
Draft이면 Ready for review 클릭
Checks 통과 확인(있다면)
충돌(conflict) 해결(있다면)
Merge pull request (권장: Squash and merge)
머지 후 main에서 3D 탭 확인
9.3 머지 후 검증(권장)
main 최신 pull
WPF 실행 → 3D 탭 표시 확인
Front/Side/Work PNG 캡처 3장 생성 확인
충돌 하이라이트 동작 확인
10. 향후 확장 포인트(추천 로드맵)
3D에서 q1..q6 슬라이더만이 아니라,
기존 TCP 기반 Pose6 모션과 연동(= IK 도입)이 필요할 수 있음
충돌 정밀도:
링크/환경을 캡슐/OBB에서 메시 기반으로 고도화(모델 파일 확보 시)
실사 이미지:
WPF 캡처 + 오프라인 렌더(Blender/Unity HDRP) 병행
11. 브랜치/PR 참고
PR #1(main 머지): 기본 MVP(2D/모션/시뮬/플러그인)
PR #2(draft/open): WPF 3D 탭(UR5 유사 링크, 랜덤 작업물, 반대편 펜스, 충돌 하이라이트, PNG 캡처)
