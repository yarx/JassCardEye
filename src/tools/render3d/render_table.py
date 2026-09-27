"""Renders counting sequences in Blender: a hand lays cards onto a pile while a phone films it.

Runs inside Blender, without a window:

    blender --background --factory-startup --python src/tools/render3d/render_table.py -- job.json

`job.json` lists the sequences to render - their parameters (see `Params` in scene.py), which frames,
and where the output goes - and how to render them. `make_dataset.py` and the Dataset Tool's
*Simulate table* write it; the format is in src/tools/render3d/README.md.

Every frame gets its label from the scene itself, not from looking at the picture: the four corners of
the top card resting on the pile, how much of it the camera sees (rays from the camera to points on the
card), and how far it smears in the image while the shutter is open. From those, the same rules as the
2D generator decide whether the frame is a positive, a negative, or in the ambiguous band the 2D
generator never draws - here it is kept out of a dataset instead (see "Only a completely visible card is
labelled" in context/architecture/data-pipeline.md).

Blender's own Python and the standard library only.
"""
from __future__ import annotations

import json
import math
import sys
import time
from pathlib import Path

import bmesh
import bpy
from bpy_extras.object_utils import world_to_camera_view
from mathutils import Matrix, Quaternion, Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
import scene as S  # noqa: E402

# The rules of the 2D generator (DatasetOptions and SceneRandomizer), applied to what the scene holds.
MARGIN_PIXELS = 3.0            # a positive lies this far clear of the border
MAX_VISIBLE_FOR_NEGATIVE = 0.85  # a top card at most this much in frame is a negative
POSITIVE_VISIBILITY = 0.95     # at most 5 % of it covered, like a card passing at the border
NEGATIVE_VISIBILITY = 0.70     # at least 30 % covered, like a card passing over the pile
SLIGHT_TRAVEL = 0.09           # smear during the shutter, in card heights: up to this is a positive
NEGATIVE_TRAVEL = 0.25         # from this on it is not reliably readable
SHUTTER = 0.5                  # of a frame: 1/60 s at 30 frames per second
SAMPLES_X, SAMPLES_Y = 12, 18  # points on the card that the visibility is measured on


# --- Building the scene ----------------------------------------------------------------------------

def reset() -> bpy.types.Scene:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    return bpy.context.scene


def rounded_rectangle(width: float, height: float, radius: float, segments: int = 6) -> list[tuple[float, float]]:
    points = []
    corners = [(width / 2 - radius, height / 2 - radius, 0), (-width / 2 + radius, height / 2 - radius, 90),
               (-width / 2 + radius, -height / 2 + radius, 180), (width / 2 - radius, -height / 2 + radius, 270)]
    for cx, cy, start in corners:
        for i in range(segments + 1):
            a = math.radians(start + 90 * i / segments)
            points.append((cx + radius * math.cos(a), cy + radius * math.sin(a)))
    return points


def card_mesh(name: str) -> bpy.types.Mesh:
    """A card as a thin slab with rounded corners: face (material 0) on top, back (1) below, edge (2)."""
    bm = bmesh.new()
    outline = rounded_rectangle(S.CARD_W, S.CARD_H, S.CORNER_RADIUS)
    top = [bm.verts.new((x, y, S.CARD_T / 2)) for x, y in outline]
    bottom = [bm.verts.new((x, y, -S.CARD_T / 2)) for x, y in outline]
    face = bm.faces.new(top)
    face.material_index = 0
    back = bm.faces.new(list(reversed(bottom)))
    back.material_index = 1
    n = len(outline)
    for i in range(n):
        side = bm.faces.new([bottom[i], bottom[(i + 1) % n], top[(i + 1) % n], top[i]])
        side.material_index = 2
    uv = bm.loops.layers.uv.new()
    for f in bm.faces:
        for loop in f.loops:
            x, y, _ = loop.vert.co
            loop[uv].uv = (x / S.CARD_W + 0.5, y / S.CARD_H + 0.5)
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    return mesh


def principled(name: str, color=(0.8, 0.8, 0.8), roughness: float = 0.5) -> tuple[bpy.types.Material, bpy.types.Node]:
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    bsdf = material.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Roughness"].default_value = roughness
    return material, bsdf


def face_material(label: str, cards_root: Path, deck: str) -> bpy.types.Material:
    suit, rank = label.split("_", 1)
    material, bsdf = principled(f"face_{label}", roughness=0.32)
    texture = material.node_tree.nodes.new("ShaderNodeTexImage")
    texture.image = bpy.data.images.load(str(cards_root / deck / suit / f"{rank}.jpg"), check_existing=True)
    texture.interpolation = "Cubic"
    material.node_tree.links.new(texture.outputs["Color"], bsdf.inputs["Base Color"])
    return material


def back_material() -> bpy.types.Material:
    # A Jass card's back: a small two-colour pattern. Only seen when a card is in the air.
    material, bsdf = principled("back", roughness=0.35)
    nodes, links = material.node_tree.nodes, material.node_tree.links
    checker = nodes.new("ShaderNodeTexChecker")
    checker.inputs["Scale"].default_value = 60
    checker.inputs["Color1"].default_value = (0.35, 0.03, 0.04, 1)
    checker.inputs["Color2"].default_value = (0.75, 0.68, 0.6, 1)
    links.new(checker.outputs["Color"], bsdf.inputs["Base Color"])
    return material


def hsv(color) -> tuple[float, float, float]:
    import colorsys
    r, g, b = colorsys.hsv_to_rgb(*color)
    return (r ** 2.2, g ** 2.2, b ** 2.2)          # sRGB to linear, near enough for cloth


def table_material(table: dict) -> bpy.types.Material:
    material, bsdf = principled("table", hsv(table["color"]), roughness=0.9)
    nodes, links = material.node_tree.nodes, material.node_tree.links
    coords = nodes.new("ShaderNodeTexCoord")
    mapping = nodes.new("ShaderNodeMapping")
    mapping.inputs["Scale"].default_value = (table["scale"],) * 3
    mapping.inputs["Rotation"].default_value = (0, 0, table["rotation"])
    links.new(coords.outputs["Object"], mapping.inputs["Vector"])
    mix = nodes.new("ShaderNodeMix")
    mix.data_type = "RGBA"
    mix.inputs[6].default_value = (*hsv(table["color"]), 1)
    mix.inputs[7].default_value = (*hsv(table["second"]), 1)
    kind = table["kind"]
    if kind == "checker":
        pattern = nodes.new("ShaderNodeTexChecker")
        links.new(mapping.outputs["Vector"], pattern.inputs["Vector"])
        pattern.inputs["Scale"].default_value = 0.4
        links.new(pattern.outputs["Fac"], mix.inputs["Factor"])
    elif kind == "tartan":
        across = nodes.new("ShaderNodeTexWave")
        along = nodes.new("ShaderNodeTexWave")
        along.bands_direction = "Y"
        for wave in (across, along):
            wave.inputs["Scale"].default_value = 0.6
            links.new(mapping.outputs["Vector"], wave.inputs["Vector"])
        maximum = nodes.new("ShaderNodeMath")
        maximum.operation = "MAXIMUM"
        links.new(across.outputs["Fac"], maximum.inputs[0])
        links.new(along.outputs["Fac"], maximum.inputs[1])
        links.new(maximum.outputs[0], mix.inputs["Factor"])
    elif kind in ("felt", "speckle"):
        noise = nodes.new("ShaderNodeTexNoise")
        noise.inputs["Scale"].default_value = 40 if kind == "felt" else 9
        noise.inputs["Detail"].default_value = 8
        links.new(mapping.outputs["Vector"], noise.inputs["Vector"])
        ramp = nodes.new("ShaderNodeValToRGB")
        ramp.color_ramp.elements[0].position = 0.35 if kind == "felt" else 0.58
        ramp.color_ramp.elements[1].position = 0.65 if kind == "felt" else 0.62
        links.new(noise.outputs["Fac"], ramp.inputs["Fac"])
        links.new(ramp.outputs["Color"], mix.inputs["Factor"])
        bump = nodes.new("ShaderNodeBump")
        bump.inputs["Strength"].default_value = 0.15
        links.new(noise.outputs["Fac"], bump.inputs["Height"])
        links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
    else:
        mix.inputs["Factor"].default_value = 0.0
    links.new(mix.outputs[2], bsdf.inputs["Base Color"])
    return material


def kelvin(temperature: float) -> tuple[float, float, float]:
    """Colour of a light of this temperature, normalised to its brightest channel."""
    t = temperature / 100
    r = 1.0 if t <= 66 else min(1, 1.2929 * (t - 60) ** -0.1332)
    g = min(1, 0.3901 * math.log(t) - 0.6318) if t <= 66 else min(1, 1.1299 * (t - 60) ** -0.0755)
    b = 1.0 if t >= 66 else (0 if t <= 19 else min(1, 0.5432 * math.log(t - 10) - 1.1962))
    return (r, max(g, 0), max(b, 0))


def build_lights(scene: bpy.types.Scene, light: dict) -> None:
    world = bpy.data.worlds.new("world")
    scene.world = world
    world.use_nodes = True
    background = world.node_tree.nodes["Background"]
    background.inputs["Strength"].default_value = light["ambient"]
    if light["kind"] == "hdri":
        folder = Path(bpy.utils.system_resource("DATAFILES", path="studiolights/world"))
        environment = world.node_tree.nodes.new("ShaderNodeTexEnvironment")
        environment.image = bpy.data.images.load(str(folder / light["hdri"]))
        mapping = world.node_tree.nodes.new("ShaderNodeMapping")
        mapping.inputs["Rotation"].default_value = (0, 0, light["hdri_rotation"])
        coords = world.node_tree.nodes.new("ShaderNodeTexCoord")
        world.node_tree.links.new(coords.outputs["Generated"], mapping.inputs["Vector"])
        world.node_tree.links.new(mapping.outputs["Vector"], environment.inputs["Vector"])
        world.node_tree.links.new(environment.outputs["Color"], background.inputs["Color"])
        return
    background.inputs["Color"].default_value = (0.8, 0.8, 0.8, 1)
    color = kelvin(light["temperature"])
    if light["kind"] == "lamp":
        data = bpy.data.lights.new("lamp", "POINT")
        data.energy = light["power"]
        data.shadow_soft_size = light["radius"]
    else:
        data = bpy.data.lights.new("sky", "AREA")
        data.energy = light["power"] * 0.6
        data.size = 1.8
    data.color = color
    lamp = bpy.data.objects.new(data.name, data)
    lamp.location = light["position"]
    scene.collection.objects.link(lamp)
    if data.type == "AREA":
        lamp.rotation_euler = (0, 0, 0)


def hand_mesh(hand: dict) -> bpy.types.Mesh:
    """A hand holding a card at its near edge, as metaballs turned into a mesh.

    Hand-local axes: x from the hand towards the card, y to its left, z up; the grip is the origin. The
    fingers lie under the card, the thumb on top of its edge, and the forearm leaves the frame.
    """
    k = hand["size"]
    ball = bpy.data.metaballs.new("hand")
    ball.resolution = 0.004
    ball.render_resolution = 0.0025
    ball.threshold = 0.5

    def ellipsoid(center, radii):
        e = ball.elements.new(type="ELLIPSOID")
        e.co = Vector(center) * k
        e.radius = 1.0
        e.size_x, e.size_y, e.size_z = (r * k for r in radii)

    def capsule(a, b, radius):
        a, b = Vector(a) * k, Vector(b) * k
        e = ball.elements.new(type="CAPSULE")
        e.co = (a + b) / 2
        e.radius = radius * k
        e.size_x = (b - a).length / 2
        e.rotation = Vector((1, 0, 0)).rotation_difference((b - a).normalized())

    # Back of the hand, the four fingers bent at the knuckles and tucked under the card's edge, the
    # thumb pressing on top of it, and the forearm.
    ellipsoid((-0.07, 0.0, 0.008), (0.046, 0.042, 0.016))
    for y, length in ((0.0215, 0.9), (0.0073, 1.0), (-0.0073, 0.97), (-0.0215, 0.82)):
        knuckle, bend = (-0.036, y, 0.006), (-0.014, y * 0.97, -0.006)
        capsule(knuckle, bend, 0.0105)
        capsule(bend, (0.004 + 0.016 * length, y * 0.93, -0.0105), 0.0095)
    capsule((-0.066, 0.038, 0.012), (-0.024, 0.028, 0.009), 0.012)
    capsule((-0.024, 0.028, 0.009), (0.012, 0.014, 0.0062), 0.0105)
    capsule((-0.10, 0.0, 0.02), (-0.45, 0.0, 0.15), 0.034)

    holder = bpy.data.objects.new("hand_balls", ball)
    bpy.context.scene.collection.objects.link(holder)
    depsgraph = bpy.context.evaluated_depsgraph_get()
    mesh = bpy.data.meshes.new_from_object(holder.evaluated_get(depsgraph))
    bpy.data.objects.remove(holder)
    return mesh


def pose_matrix(position, heading_or_yaw: float, tilt: float, tilt_heading: float) -> Matrix:
    """Turn about z by `heading_or_yaw`, then tilt about the horizontal axis left of `tilt_heading`."""
    axis = Vector((-math.sin(tilt_heading), math.cos(tilt_heading), 0))
    rotation = Matrix.Rotation(tilt, 4, axis) @ Matrix.Rotation(heading_or_yaw, 4, "Z")
    return Matrix.Translation(position) @ rotation


def key(obj: bpy.types.Object, matrix: Matrix, frame: int, previous: Quaternion | None) -> Quaternion:
    location, rotation, _ = matrix.decompose()
    if previous is not None and rotation.dot(previous) < 0:
        rotation.negate()                       # no half turn between two keys that mean the same pose
    obj.location = location
    obj.rotation_quaternion = rotation
    obj.keyframe_insert("location", frame=frame)
    obj.keyframe_insert("rotation_quaternion", frame=frame)
    return rotation


def build(scene_data: dict, cards_root: Path, engine: str, samples: int) -> dict:
    scene = reset()
    params = scene_data["params"]
    fps, frames = params["fps"], scene_data["frames"]
    scene.render.fps = fps
    scene.frame_start, scene.frame_end = 0, frames - 1
    scene.render.resolution_x = scene.render.resolution_y = params["size"]
    scene.render.resolution_percentage = 100
    scene.render.use_motion_blur = True
    scene.render.motion_blur_shutter = SHUTTER
    scene.render.image_settings.file_format = "JPEG"
    scene.render.image_settings.quality = 90
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.exposure = scene_data["exposure"]
    configure_engine(scene, engine, samples)

    # Table and lights.
    bpy.ops.mesh.primitive_plane_add(size=8.0)
    table = bpy.context.active_object
    table.data.materials.append(table_material(scene_data["table"]))
    build_lights(scene, scene_data["light"])

    back, edge = back_material(), principled("edge", (0.85, 0.84, 0.8), 0.6)[0]
    deck = params["deck"]

    def card_object(label: str) -> bpy.types.Object:
        mesh = card_mesh(f"card_{label}")
        for m in (face_material(label, cards_root, deck), back, edge):
            mesh.materials.append(m)
        obj = bpy.data.objects.new(label, mesh)
        scene.collection.objects.link(obj)
        obj.rotation_mode = "QUATERNION"
        return obj

    pile = []
    for i, card in enumerate(scene_data["pile"]):
        obj = card_object(card["label"])
        obj.matrix_world = pose_matrix((card["x"], card["y"], S.CARD_T * (i + 0.5)), card["rotation"], 0, 0)
        pile.append(obj)
    laid = [card_object(card["label"]) for card in scene_data["laid"]]

    hand = None
    if scene_data["hand"] is not None:
        mesh = hand_mesh(scene_data["hand"])
        skin, bsdf = principled("skin", scene_data["hand"]["skin"], 0.55)
        bsdf.inputs["Subsurface Weight"].default_value = 0.25
        mesh.materials.append(skin)
        hand = bpy.data.objects.new("hand", mesh)
        hand.rotation_mode = "QUATERNION"
        scene.collection.objects.link(hand)

    camera_data = bpy.data.cameras.new("phone")
    camera_data.angle = scene_data["camera"]["fov"]
    camera_data.clip_start = 0.005
    camera = bpy.data.objects.new("phone", camera_data)
    camera.rotation_mode = "QUATERNION"
    scene.collection.objects.link(camera)
    scene.camera = camera

    # The phone itself: not in its own picture, but in the lamp's way, so its shadow falls on the table.
    bpy.ops.mesh.primitive_cube_add(size=1.0)
    body = bpy.context.active_object
    body.scale = (0.075, 0.155, 0.008)
    body.data.materials.append(principled("phone", (0.02, 0.02, 0.02), 0.3)[0])
    body.visible_camera = False
    body.parent = camera
    body.location = (0.0, -0.05, 0.012)                  # the lens sits near the top of the phone

    # Every object that moves gets a key on every frame, from the poses scene.py computes.
    previous: dict[str, Quaternion | None] = {}
    for f in range(frames):
        t = f / fps
        position, target = S.camera_pose(scene_data["camera"], t, scene_data["duration"])
        direction = Vector(target) - Vector(position)
        look = direction.to_track_quat("-Z", "Y") @ Quaternion((0, 0, 1), scene_data["camera"]["roll"])
        previous["phone"] = key(camera, Matrix.Translation(position) @ look.to_matrix().to_4x4(), f,
                                previous.get("phone"))

        for card, obj in zip(scene_data["laid"], laid):
            (x, y, z), (pitch, _, yaw) = S.laid_card_pose(card, t)
            heading = card["entry"] + math.pi
            previous[obj.name] = key(obj, pose_matrix((x, y, z), yaw, pitch, heading), f, previous.get(obj.name))

        if hand is not None:
            active = None
            for card in scene_data["laid"]:
                pose = S.hand_pose(card, t)
                if pose is not None:
                    active = pose
            if active is None:
                matrix = Matrix.Translation((0, 0, -1.0))  # under the table while no card is being laid
            else:
                (gx, gy, gz), heading, pitch = active
                matrix = pose_matrix((gx, gy, gz), heading, pitch, heading)
            previous["hand"] = key(hand, matrix, f, previous.get("hand"))

    for obj in [camera, *laid, *([hand] if hand else [])]:
        if obj.animation_data and obj.animation_data.action:
            for curve in fcurves(obj.animation_data):
                for point in curve.keyframe_points:
                    point.interpolation = "LINEAR"
    return {"scene": scene, "camera": camera, "pile": pile, "laid": laid}


def fcurves(animation_data):
    """The F-curves of an object's action, in Blender's layered actions and in the older flat ones."""
    action = animation_data.action
    if hasattr(action, "fcurves") and len(action.fcurves):
        return list(action.fcurves)
    curves = []
    for layer in getattr(action, "layers", []):
        for strip in layer.strips:
            for bag in strip.channelbags:
                curves.extend(bag.fcurves)
    return curves


def configure_engine(scene: bpy.types.Scene, engine: str, samples: int) -> None:
    if engine == "eevee":
        scene.render.engine = "BLENDER_EEVEE"
        scene.eevee.taa_render_samples = samples
        return
    scene.render.engine = "CYCLES"
    cycles = scene.cycles
    cycles.samples = samples
    cycles.use_adaptive_sampling = True
    cycles.adaptive_threshold = 0.05
    cycles.use_denoising = True
    cycles.max_bounces = 4
    preferences = bpy.context.preferences.addons["cycles"].preferences
    for device_type in ("OPTIX", "CUDA", "METAL", "HIP", "ONEAPI"):
        try:
            preferences.compute_device_type = device_type
        except TypeError:
            continue
        preferences.get_devices()
        gpus = [d for d in preferences.devices if d.type == device_type]
        if gpus:
            for d in preferences.devices:
                d.use = d.type == device_type
            cycles.device = "GPU"
            return
    cycles.device = "CPU"


def device_name(scene: bpy.types.Scene) -> str:
    if scene.render.engine != "CYCLES":
        return "EEVEE"
    if scene.cycles.device == "CPU":
        return "Cycles CPU"
    preferences = bpy.context.preferences.addons["cycles"].preferences
    names = sorted({d.name for d in preferences.devices if d.use})
    return f"Cycles {preferences.compute_device_type} ({', '.join(names)})"


# --- Labels ----------------------------------------------------------------------------------------

def top_card(scene_data: dict, built: dict, t: float) -> bpy.types.Object | None:
    """The card lying on top of the pile at time t - a laid card counts once it has settled."""
    top = built["pile"][-1] if built["pile"] else None
    for card, obj in zip(scene_data["laid"], built["laid"]):
        if S.laid_card_resting(card, t):
            top = obj
    return top


def card_corners(obj: bpy.types.Object) -> list[Vector]:
    """Top left, top right, bottom right, bottom left of the card's face, as the scan has them."""
    w, h, z = S.CARD_W / 2, S.CARD_H / 2, S.CARD_T / 2
    return [obj.matrix_world @ Vector(c) for c in ((-w, h, z), (w, h, z), (w, -h, z), (-w, -h, z))]


def to_pixels(scene, camera, point: Vector) -> tuple[float, float, float]:
    v = world_to_camera_view(scene, camera, point)
    size = scene.render.resolution_x
    return v.x * size, (1 - v.y) * size, v.z


def visibility(scene, camera, obj) -> float:
    """Share of the card's face the camera sees: inside the frame and not covered by anything."""
    depsgraph = bpy.context.evaluated_depsgraph_get()
    origin = camera.matrix_world.translation
    w, h, z = S.CARD_W, S.CARD_H, S.CARD_T / 2 + 1e-5
    size = scene.render.resolution_x
    seen = 0
    for i in range(SAMPLES_X):
        for j in range(SAMPLES_Y):
            local = Vector(((i + 0.5) / SAMPLES_X * w - w / 2, (j + 0.5) / SAMPLES_Y * h - h / 2, z))
            point = obj.matrix_world @ local
            x, y, depth = to_pixels(scene, camera, point)
            if depth <= 0 or not (0 <= x < size and 0 <= y < size):
                continue
            direction = point - origin
            hit, _, _, _, hit_obj, _ = scene.ray_cast(depsgraph, origin, direction.normalized(),
                                                      distance=direction.length + 1e-4)
            if hit and hit_obj.name == obj.name:
                seen += 1
    return seen / (SAMPLES_X * SAMPLES_Y)


def in_frame_share(corners: list[tuple[float, float]], size: int) -> float:
    xs, ys = [c[0] for c in corners], [c[1] for c in corners]
    full = (max(xs) - min(xs)) * (max(ys) - min(ys))
    if full <= 0:
        return 0.0
    w = max(0.0, min(max(xs), size) - max(min(xs), 0))
    h = max(0.0, min(max(ys), size) - max(min(ys), 0))
    return w * h / full


def label_frame(scene_data: dict, built: dict, f: int) -> dict:
    scene, camera = built["scene"], built["camera"]
    fps, size = scene_data["params"]["fps"], scene_data["params"]["size"]
    t = f / fps

    def corners_at(obj, subframe: float):
        scene.frame_set(f + math.floor(subframe), subframe=subframe - math.floor(subframe))
        return [to_pixels(scene, camera, c)[:2] for c in card_corners(obj)]

    flying = any(not S.laid_card_resting(card, t) and card["start"] <= t for card in scene_data["laid"])
    obj = top_card(scene_data, built, t)
    record = {"frame": f, "time": round(t, 4), "flying_card": flying}
    if obj is None:
        scene.frame_set(f)
        record.update(kind="negative", reason="no card on the table", card=None)
        return record

    # How far the corners travel while the shutter is open, in card heights on the picture.
    before = corners_at(obj, -SHUTTER / 2)
    seen_before = visibility(scene, camera, obj)
    after = corners_at(obj, SHUTTER / 2)
    seen_after = visibility(scene, camera, obj)
    scene.frame_set(f)
    corners = [to_pixels(scene, camera, c)[:2] for c in card_corners(obj)]
    height = max(1e-6, 0.5 * (math.dist(corners[0], corners[3]) + math.dist(corners[1], corners[2])))
    travel = max(math.dist(a, b) for a, b in zip(before, after)) / height
    # Covered at any moment the shutter is open counts: a hand smearing across the card hides it too.
    seen = min(visibility(scene, camera, obj), seen_before, seen_after)
    inside = all(MARGIN_PIXELS <= x <= size - MARGIN_PIXELS and MARGIN_PIXELS <= y <= size - MARGIN_PIXELS
                 for x, y in corners)
    share = in_frame_share(corners, size)
    motion = "still" if travel <= 0.02 else "slight" if travel <= SLIGHT_TRAVEL else \
        "moving" if travel >= NEGATIVE_TRAVEL else "blurred"

    if not inside:
        kind, reason = ("negative", "cut off by the border") if share <= MAX_VISIBLE_FOR_NEGATIVE \
            else ("difficult", "touches the border")
    elif travel >= NEGATIVE_TRAVEL:
        kind, reason = "negative", "smeared by motion"
    elif seen <= NEGATIVE_VISIBILITY:
        kind, reason = "negative", "covered"
    elif seen >= POSITIVE_VISIBILITY and travel <= SLIGHT_TRAVEL:
        kind, reason = "positive", "lies still and free"
    else:
        kind, reason = "difficult", "partly covered" if seen < POSITIVE_VISIBILITY else "blurred"

    record.update(kind=kind, reason=reason, card=obj.name, class_id=S.class_id(obj.name),
                  corners=[[round(x, 2), round(y, 2)] for x, y in corners],
                  visibility=round(seen, 3), in_frame=round(share, 3), travel=round(travel, 3), motion=motion)
    return record


# --- Output ----------------------------------------------------------------------------------------

def detect_line(record: dict, size: int) -> str:
    xs = [min(max(c[0], 0), size) for c in record["corners"]]
    ys = [min(max(c[1], 0), size) for c in record["corners"]]
    cx, cy = (min(xs) + max(xs)) / 2 / size, (min(ys) + max(ys)) / 2 / size
    w, h = (max(xs) - min(xs)) / size, (max(ys) - min(ys)) / size
    return f"{record['class_id']} {cx:.6f} {cy:.6f} {w:.6f} {h:.6f}\n"


def locate_line(record: dict, size: int) -> str:
    values = " ".join(f"{min(max(v / size, 0.0), 1.0):.6f}" for c in record["corners"] for v in c)
    return f"0 {values}\n"


def render_sequence(job: dict, sequence: dict) -> dict:
    params = S.Params(**sequence["params"])
    scene_data = S.resolve(params)
    cards_root = Path(job["cards_root"])
    started = time.perf_counter()
    built = build(scene_data, cards_root, job.get("engine", "cycles"), job.get("samples", 32))
    build_seconds = time.perf_counter() - started
    scene = built["scene"]
    size = params.size
    output = Path(job["output"])
    dataset = job.get("mode") == "dataset"
    stem = sequence.get("stem", f"seed{params.seed}")
    frames = sequence.get("frames") or list(range(scene_data["frames"]))

    records, render_seconds = [], []
    for f in frames:
        record = label_frame(scene_data, built, f)
        records.append(record)
        if dataset and record["kind"] == "difficult":
            continue                          # the ambiguous band stays out of a dataset, unrendered
        name = f"{stem}_{f:03d}" if dataset else f"{f:03d}"
        target = output / "images" / f"{name}.jpg" if dataset else output / stem / f"{name}.jpg"
        target.parent.mkdir(parents=True, exist_ok=True)
        scene.frame_set(f)
        scene.render.filepath = str(target)
        tick = time.perf_counter()
        bpy.ops.render.render(write_still=True)
        render_seconds.append(time.perf_counter() - tick)
        record["image"] = str(target.relative_to(output))
        print(f"render3d: frame {f} of {len(frames)} {record['kind']}", flush=True)   # progress for the tool
        if dataset and record["kind"] == "positive":
            for folder, line in (("detect", detect_line(record, size)), ("locate", locate_line(record, size))):
                labels = output / folder / "labels"
                labels.mkdir(parents=True, exist_ok=True)
                (labels / f"{name}.txt").write_text(line)

    summary = {
        "stem": stem,
        "scene": scene_data,
        "frames": records,
        "device": device_name(scene),
        "engine": job.get("engine", "cycles"),
        "samples": job.get("samples", 32),
        "build_seconds": round(build_seconds, 3),
        "render_seconds": [round(s, 4) for s in render_seconds],
    }
    if not dataset:
        (output / stem).mkdir(parents=True, exist_ok=True)
        (output / stem / "frames.json").write_text(json.dumps(summary, indent=1))
    return summary


def main() -> int:
    arguments = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if len(arguments) != 1:
        print("usage: blender --background --factory-startup --python render_table.py -- job.json", file=sys.stderr)
        return 2
    job = json.loads(Path(arguments[0]).read_text())
    output = Path(job["output"])
    output.mkdir(parents=True, exist_ok=True)
    log = output / "render3d.jsonl" if job.get("mode") == "dataset" else None
    for sequence in job["sequences"]:
        summary = render_sequence(job, sequence)
        if log is not None:
            slim = {k: summary[k] for k in ("stem", "device", "engine", "samples", "build_seconds", "render_seconds")}
            slim["frames"] = [{k: r.get(k) for k in ("frame", "kind", "reason", "card", "visibility", "travel", "motion", "image")}
                              for r in summary["frames"]]
            with log.open("a") as handle:
                handle.write(json.dumps(slim) + "\n")
        print(f"render3d: {summary['stem']}: {len(summary['render_seconds'])} frames on {summary['device']}, "
              f"{sum(summary['render_seconds']):.1f} s", flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
