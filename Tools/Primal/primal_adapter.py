"""CoMPaSS JSON adapter for the original gsartoretti/PRIMAL public checkpoint.
Uses upstream ACNet and observe(), without substituting a rule policy for the trained model.
Install the isolated legacy environment described in README.md before running.
"""
import contextlib
import hashlib
import json
import os
from pathlib import Path
import sys
import time
import ast
import types


def observation_functions(repository, numpy):
    # Use upstream function bodies without importing its Tkinter demonstration UI.
    source = (repository / 'mapgenerator.py').read_text(encoding='utf-8')
    parsed = ast.parse(source)
    selected = [node for node in parsed.body if isinstance(node, ast.FunctionDef) and node.name in ('observe', 'getDir')]
    namespace = {'np': numpy}
    for node in parsed.body:
        if isinstance(node, ast.Assign) and any(isinstance(t, ast.Name) and t.id == 'dirDict' for t in node.targets):
            namespace['dirDict'] = ast.literal_eval(node.value)
    if len(selected) != 2 or 'dirDict' not in namespace:
        raise RuntimeError('Unsupported upstream observation source version')
    module = ast.Module(body=selected)
    exec(compile(ast.fix_missing_locations(module), str(repository / 'mapgenerator.py'), 'exec'), namespace)
    return types.SimpleNamespace(observe=namespace['observe'], getDir=namespace['getDir'])


def infer(request, config):
    import numpy as np
    import tensorflow as tf
    if not tf.__version__.startswith("1."):
        raise RuntimeError("Original PRIMAL requires TensorFlow 1.x (tf.contrib). Use the separate legacy environment.")
    repo = Path(config["repository"]).expanduser().resolve()
    sys.path.insert(0, str(repo))
    upstream = observation_functions(repo, np)
    from ACNet import ACNet
    checkpoint_dir = Path(config["checkpoint"]).expanduser().resolve()
    checkpoint = tf.train.latest_checkpoint(str(checkpoint_dir))
    if not checkpoint:
        raise RuntimeError("No public model checkpoint found in " + str(checkpoint_dir))
    # TensorFlow checkpoint metadata can contain a path from the original training machine.
    checkpoint = str(checkpoint_dir / Path(checkpoint).name)
    if not Path(checkpoint + ".index").exists():
        raise RuntimeError("Missing checkpoint .index/.data files: " + checkpoint)
    seed = int(request.get("seed", 23)) & 0x7fffffff
    np.random.seed(seed)
    tf.set_random_seed(seed)
    width, height = int(request["width"]), int(request["height"])
    agents = request["agents"]
    if not 1 <= width <= 64 or not 1 <= height <= 64 or not 1 <= len(agents) <= 32:
        raise ValueError("Unsupported map dimensions or agent count")
    blocked = request["blockedCells"]
    if len(blocked) != width * height or any(v not in "01" for v in blocked):
        raise ValueError("Invalid obstacle data")
    class World:
        pass
    world = World()
    world.state = -np.array([int(v) for v in blocked], dtype=int).reshape(height, width)
    world.goals = np.zeros((height, width), dtype=int)
    world.agent_positions = [(a["startY"], a["startX"]) for a in agents]
    world.agent_goals = [(a["goalY"], a["goalX"]) for a in agents]
    for i, (position, goal) in enumerate(zip(world.agent_positions, world.agent_goals)):
        world.state[position] = i + 1
        world.goals[goal] = i + 1
    positions = [[p] for p in world.agent_positions]
    arrived = [p == g for p, g in zip(world.agent_positions, world.agent_goals)]
    observations, logs = [], []
    begun = time.perf_counter()
    model_hash = hashlib.sha256(Path(checkpoint + ".index").read_bytes()).hexdigest()
    failure = "Maximum rollout steps reached"
    cpu = tf.ConfigProto(device_count={"GPU": 0}, intra_op_parallelism_threads=2, inter_op_parallelism_threads=1)
    with tf.Session(config=cpu) as session:
        network = ACNet("global", 5, None, False, 10, "global")
        tf.train.Saver().restore(session, checkpoint)
        states = [[np.copy(x) for x in network.state_init] for _ in agents]
        for step in range(min(1024, max(1, int(request.get("maxTimeSteps", 256))))):
            if all(arrived):
                failure = ""
                break
            targets = []
            for i, a in enumerate(agents):
                current = world.agent_positions[i]
                if arrived[i]:
                    targets.append(current)
                    continue
                observation, vector = upstream.observe(world, i + 1, True)
                policy, new_state = session.run([network.policy, network.state_out], feed_dict={
                    network.inputs: [observation], network.goal_pos: [vector],
                    network.state_in[0]: states[i][0], network.state_in[1]: states[i][1]})
                states[i] = new_state
                probabilities = np.ravel(policy)
                action = int(np.argmax(probabilities))
                dy, dx = upstream.getDir(action)
                target = (current[0] + dy, current[1] + dx)
                blocked_action = not (0 <= target[0] < height and 0 <= target[1] < width) or (0 <= target[0] < height and 0 <= target[1] < width and world.state[target] < 0)
                if blocked_action:
                    target = current
                targets.append(target)
                observations.append({"agentId": a["id"], "step": step, "action": action, "size": 10,
                    "probabilities": [float(v) for v in probabilities], "channels": np.asarray(observation, dtype=float).reshape(-1).tolist(),
                    "goalVector": [float(v) for v in vector], "note": "blocked action → wait" if blocked_action else "model argmax"})
                logs.append("t={} Agent {}: action {}{}".format(step, a["id"], action, " blocked; wait" if blocked_action else ""))
            conflicts = []
            for i in range(len(agents)):
                for j in range(i + 1, len(agents)):
                    if targets[i] == targets[j] or (targets[i] == world.agent_positions[j] and targets[j] == world.agent_positions[i]):
                        conflicts.append("t={} agents {}/{} vertex/edge conflict; rollout stopped before move".format(step + 1, agents[i]["id"], agents[j]["id"]))
            if conflicts:
                logs.extend(conflicts)
                failure = "Predicted joint-action collision: " + conflicts[0]
                break
            for position in world.agent_positions:
                world.state[position] = 0
            world.agent_positions = targets
            for i, position in enumerate(targets):
                world.state[position] = i + 1
                positions[i].append(position)
                arrived[i] = arrived[i] or position == world.agent_goals[i]
        if all(arrived):
            failure = ""
    paths = []
    for a, history, goal in zip(agents, positions, world.agent_goals):
        # Remove post-arrival waits from the cost; Unity retains goal occupancy itself.
        if goal in history:
            history = history[:history.index(goal) + 1]
        paths.append({"agentId": a["id"], "x": [p[1] for p in history], "y": [p[0] for p in history]})
    return {"protocolVersion": 1, "success": not failure, "failureReason": failure,
        "modelSource": "https://github.com/gsartoretti/PRIMAL; checkpoint-index-sha256=" + model_hash,
        "directlyTrained": False, "inferenceMilliseconds": (time.perf_counter() - begun) * 1000,
        "paths": paths, "actionLogs": logs, "decisions": observations}


def main():
    try:
        request = json.loads(sys.stdin.readline())
        local_config = Path(__file__).with_name('config.json')
        default_config = Path(os.environ.get('LOCALAPPDATA', str(Path.home()))) / 'CoMPaSS' / 'PrimalRuntime' / 'config.json'
        configuration = Path(os.environ.get("COMPASS_PRIMAL_CONFIG", str(local_config if local_config.exists() else default_config)))
        config = json.loads(configuration.read_text(encoding="utf-8"))
        with contextlib.redirect_stdout(sys.stderr):
            response = infer(request, config)
    except Exception as error:
        response = {"protocolVersion": 1, "success": False, "failureReason": str(error), "paths": [], "actionLogs": []}
    print(json.dumps(response, ensure_ascii=True), flush=True)


if __name__ == "__main__":
    main()
