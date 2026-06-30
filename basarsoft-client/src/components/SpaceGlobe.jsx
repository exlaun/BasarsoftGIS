import {
  forwardRef,
  useEffect,
  useImperativeHandle,
  useRef,
  useState,
} from 'react'
import * as THREE from 'three'
import './SpaceGlobe.css'

// Turkey (used for the focus point of the fly-to cinematic).
const TURKEY_LAT = 38.9637
const TURKEY_LNG = 35.2433

const IDLE_SPIN = 0.00045 // radians per frame while idling
const FLY_DURATION = 1900 // ms for the dolly + spin-to-Turkey + plunge

// Camera framing: the globe (radius 1) should fill this fraction of the
// viewport *height* at rest. 0.8 → a big, centered planet.
const FOV = 30
const TARGET_FILL = 0.82

const easeInOutCubic = (t) =>
  t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2
const easeOutCubic = (t) => 1 - Math.pow(1 - t, 3)
const easeInCubic = (t) => t * t * t
const clamp01 = (t) => Math.max(0, Math.min(1, t))

const prefersReducedMotion = () =>
  typeof window !== 'undefined' &&
  window.matchMedia('(prefers-reduced-motion: reduce)').matches

// Convert a lat/lng to a point on a unit sphere whose equirectangular texture
// has its left edge at lon -180 (matches three's default SphereGeometry UVs and
// the blue-marble texture). Used to spin Turkey to face the camera (+z).
function latLngToVector3(lat, lng) {
  const phi = ((lng + 180) * Math.PI) / 180 // azimuth
  const theta = ((90 - lat) * Math.PI) / 180 // polar angle from +y
  return new THREE.Vector3(
    -Math.cos(phi) * Math.sin(theta),
    Math.cos(theta),
    Math.sin(phi) * Math.sin(theta),
  )
}

const SpaceGlobe = forwardRef(function SpaceGlobe(_, ref) {
  const canvasRef = useRef(null)
  const veilRef = useRef(null)

  // three.js handles live in refs so the per-frame loop never re-renders React.
  const worldRef = useRef(null)
  const cloudsRef = useRef(null)
  const cameraRef = useRef(null)
  const restDistRef = useRef(5)
  const targetQuatRef = useRef(null)

  const modeRef = useRef('idle') // 'idle' | 'flying' | 'done'
  const flyStartRef = useRef(0)
  const startQuatRef = useRef(null)
  const flyResolveRef = useRef(null)
  const flyRafRef = useRef(0)

  const [globeFailed, setGlobeFailed] = useState(false)

  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas) return undefined

    const reduced = prefersReducedMotion()

    let renderer
    try {
      renderer = new THREE.WebGLRenderer({
        canvas,
        antialias: true,
        alpha: true, // let the CSS starfield/gradient show through
      })
    } catch {
      setGlobeFailed(true)
      return undefined
    }
    renderer.setClearColor(0x000000, 0)
    renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2))
    renderer.outputColorSpace = THREE.SRGBColorSpace

    const scene = new THREE.Scene()

    const camera = new THREE.PerspectiveCamera(
      FOV,
      window.innerWidth / window.innerHeight,
      0.1,
      100,
    )
    const fovRad = (FOV * Math.PI) / 180
    const restDist = 1 / (TARGET_FILL * Math.tan(fovRad / 2))
    camera.position.set(0, 0, restDist)
    cameraRef.current = camera
    restDistRef.current = restDist

    // Lighting: a near-frontal sun keeps most of the visible disc lit (like the
    // blue-marble photo) with a soft terminator; ambient lifts the night side.
    scene.add(new THREE.AmbientLight(0xffffff, 0.55))
    const sun = new THREE.DirectionalLight(0xffffff, 1.5)
    sun.position.set(2, 1, 3)
    scene.add(sun)

    const loader = new THREE.TextureLoader()
    const dayTex = loader.load('/textures/earth-blue-marble.jpg')
    dayTex.colorSpace = THREE.SRGBColorSpace
    const waterTex = loader.load('/textures/earth-water.png')
    const topoTex = loader.load('/textures/earth-topology.png')
    const cloudsTex = loader.load('/textures/earth-clouds.png')
    cloudsTex.colorSpace = THREE.SRGBColorSpace

    const world = new THREE.Group()
    scene.add(world)
    worldRef.current = world

    // Earth.
    const earth = new THREE.Mesh(
      new THREE.SphereGeometry(1, 96, 96),
      new THREE.MeshPhongMaterial({
        map: dayTex,
        specularMap: waterTex, // oceans catch the light
        specular: new THREE.Color(0x2a3a55),
        shininess: 14,
        bumpMap: topoTex,
        bumpScale: 0.025,
      }),
    )
    world.add(earth)

    // Clouds, just above the surface.
    const clouds = new THREE.Mesh(
      new THREE.SphereGeometry(1.012, 96, 96),
      new THREE.MeshPhongMaterial({
        map: cloudsTex,
        transparent: true,
        opacity: 0.85,
        depthWrite: false,
      }),
    )
    world.add(clouds)
    cloudsRef.current = clouds

    // Atmosphere rim glow (classic fresnel shell, additive).
    const atmosphere = new THREE.Mesh(
      new THREE.SphereGeometry(1, 96, 96),
      new THREE.ShaderMaterial({
        vertexShader: `
          varying vec3 vNormal;
          void main() {
            vNormal = normalize(normalMatrix * normal);
            gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
          }
        `,
        fragmentShader: `
          varying vec3 vNormal;
          void main() {
            // Tight, dim rim that hugs the limb — a hint of atmosphere, not a glow ring.
            float intensity = pow(0.5 - dot(vNormal, vec3(0.0, 0.0, 1.0)), 6.0);
            gl_FragColor = vec4(0.22, 0.42, 0.78, 1.0) * intensity * 0.5;
          }
        `,
        blending: THREE.AdditiveBlending,
        side: THREE.BackSide,
        transparent: true,
        depthWrite: false,
      }),
    )
    atmosphere.scale.setScalar(1.06)
    scene.add(atmosphere) // outside `world` so the rim stays camera-aligned

    // Pre-compute the orientation that brings Turkey to face the camera (+z).
    const turkeyDir = latLngToVector3(TURKEY_LAT, TURKEY_LNG).normalize()
    targetQuatRef.current = new THREE.Quaternion().setFromUnitVectors(
      turkeyDir,
      new THREE.Vector3(0, 0, 1),
    )

    const setSize = () => {
      renderer.setSize(window.innerWidth, window.innerHeight, false)
      camera.aspect = window.innerWidth / window.innerHeight
      camera.updateProjectionMatrix()
    }
    setSize()

    const veil = veilRef.current

    const animate = (now) => {
      if (modeRef.current === 'idle') {
        if (!reduced && !document.hidden) {
          world.rotation.y += IDLE_SPIN
          clouds.rotation.y += IDLE_SPIN * 0.18 // drift a touch faster
        }
      } else if (modeRef.current === 'flying') {
        const p = Math.min(1, (now - flyStartRef.current) / FLY_DURATION)

        // A. Approach: gentle dolly toward the planet.
        const eApproach = easeOutCubic(clamp01(p / 0.35))
        // B. Focus: spin so Turkey faces the camera.
        const eFocus = easeInOutCubic(clamp01((p - 0.15) / 0.65))
        // C. Plunge: dive into Turkey's location.
        const ePlunge = easeInCubic(clamp01((p - 0.55) / 0.45))

        world.quaternion.slerpQuaternions(
          startQuatRef.current,
          targetQuatRef.current,
          eFocus,
        )

        const rest = restDistRef.current
        const approachDist = rest * 0.82
        const plungeDist = 1.06 // just above the surface
        camera.position.z =
          rest +
          (approachDist - rest) * eApproach +
          (plungeDist - approachDist) * ePlunge

        if (veil) veil.style.opacity = String(clamp01((p - 0.7) / 0.3))

        if (p >= 1) {
          modeRef.current = 'done'
          if (flyResolveRef.current) flyResolveRef.current()
        }
      }

      renderer.render(scene, camera)
    }

    if (reduced) {
      renderer.render(scene, camera) // one static frame
    } else {
      renderer.setAnimationLoop(animate)
    }

    let resizeTimer = 0
    const onResize = () => {
      clearTimeout(resizeTimer)
      resizeTimer = setTimeout(setSize, 150)
    }
    window.addEventListener('resize', onResize)

    return () => {
      clearTimeout(resizeTimer)
      cancelAnimationFrame(flyRafRef.current)
      window.removeEventListener('resize', onResize)
      renderer.setAnimationLoop(null)
      renderer.dispose()
      scene.traverse((obj) => {
        if (obj.geometry) obj.geometry.dispose()
        if (obj.material) {
          const mats = Array.isArray(obj.material) ? obj.material : [obj.material]
          mats.forEach((m) => m.dispose())
        }
      })
      ;[dayTex, waterTex, topoTex, cloudsTex].forEach((t) => t.dispose())
    }
  }, [])

  useImperativeHandle(ref, () => ({
    // Dollies toward the planet, spins Turkey to face the camera, plunges into
    // it, and raises a dark veil — resolving when done so the caller can
    // navigate to the map.
    flyToTurkey: () =>
      new Promise((resolve) => {
        const reduced = prefersReducedMotion()
        const veil = veilRef.current

        if (reduced || globeFailed || !worldRef.current) {
          // Reduced motion / no globe: just fade the veil in.
          const start = performance.now()
          const fade = (now) => {
            const p = Math.min(1, (now - start) / 500)
            if (veil) veil.style.opacity = String(p)
            if (p < 1) flyRafRef.current = requestAnimationFrame(fade)
            else resolve()
          }
          flyRafRef.current = requestAnimationFrame(fade)
          return
        }

        // Hand the flight off to the live render loop.
        flyResolveRef.current = resolve
        flyStartRef.current = performance.now()
        startQuatRef.current = worldRef.current.quaternion.clone()
        modeRef.current = 'flying'
      }),
  }))

  return (
    <div className="space-globe" aria-hidden="true">
      <div className="space-globe__nebula" />
      <div className="space-globe__stars" />
      <canvas
        ref={canvasRef}
        className="space-globe__canvas"
        style={{ display: globeFailed ? 'none' : 'block' }}
      />
      <div ref={veilRef} className="space-globe__veil" />
    </div>
  )
})

export default SpaceGlobe
