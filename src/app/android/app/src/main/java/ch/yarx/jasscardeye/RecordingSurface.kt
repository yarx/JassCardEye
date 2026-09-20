package ch.yarx.jasscardeye

import android.graphics.Bitmap
import android.opengl.EGL14
import android.opengl.EGLConfig
import android.opengl.EGLContext
import android.opengl.EGLDisplay
import android.opengl.EGLExt
import android.opengl.EGLSurface
import android.opengl.GLES20
import android.opengl.GLUtils
import android.view.Surface
import java.nio.ByteBuffer
import java.nio.ByteOrder

// Android only.

/**
 * Draws the analysed square into the encoder's input surface, stamped with a presentation time of our choosing.
 *
 * Drawing through `Surface.lockHardwareCanvas` would stamp a frame with the moment it is posted. The
 * recognition log needs to know which submitted frame the encoder actually wrote, so that a row is
 * written only for a frame that is in the video, under that frame's number. A time set per frame with
 * `eglPresentationTimeANDROID` comes back unchanged on the encoder's output, which makes that match exact - and
 * setting it takes an EGL context of our own.
 *
 * Confined to the analysis thread, like [SessionRecorder]: created, drawn into and closed there. That thread also
 * runs the recogniser, and LiteRT's GPU delegate may keep an EGL context of its own current on it. So this class
 * leaves the thread as it found it: every use of its context is wrapped in [withOwnContext], which puts back
 * whatever was current before, and closing never releases the thread's EGL state or terminates the shared default
 * display. Without that, a recording could take the detector's context away mid-session, and the recogniser would
 * report every later frame as "no card".
 */
internal class RecordingSurface(surface: Surface, private val side: Int) : AutoCloseable {
    private var display: EGLDisplay = EGL14.EGL_NO_DISPLAY
    private var context: EGLContext = EGL14.EGL_NO_CONTEXT
    private var window: EGLSurface = EGL14.EGL_NO_SURFACE
    private var program = 0
    private var texture = 0
    private var positionAttribute = -1
    private var uvAttribute = -1

    /** Two triangles over the whole surface: x, y and the texture coordinate per corner, the bitmap's top row on top. */
    private val quad = ByteBuffer.allocateDirect(16 * Float.SIZE_BYTES).order(ByteOrder.nativeOrder()).asFloatBuffer().apply {
        put(
            floatArrayOf(
                -1f, -1f, 0f, 1f,
                1f, -1f, 1f, 1f,
                -1f, 1f, 0f, 0f,
                1f, 1f, 1f, 0f,
            ),
        )
        position(0)
    }

    init {
        try {
            display = EGL14.eglGetDisplay(EGL14.EGL_DEFAULT_DISPLAY)
            check(EGL14.eglInitialize(display, null, 0, null, 0)) { "EGL konnte nicht gestartet werden" }
            val config = chooseConfig()
            context = EGL14.eglCreateContext(
                display, config, EGL14.EGL_NO_CONTEXT, intArrayOf(EGL14.EGL_CONTEXT_CLIENT_VERSION, 2, EGL14.EGL_NONE), 0,
            )
            check(context != EGL14.EGL_NO_CONTEXT) { "EGL-Kontext konnte nicht angelegt werden" }
            window = EGL14.eglCreateWindowSurface(display, config, surface, intArrayOf(EGL14.EGL_NONE), 0)
            check(window != EGL14.EGL_NO_SURFACE) { "Encoder-Fläche konnte nicht angelegt werden" }
            withOwnContext {
                program = link()
                positionAttribute = GLES20.glGetAttribLocation(program, "position")
                uvAttribute = GLES20.glGetAttribLocation(program, "uv")
                texture = createTexture()
            }
        } catch (error: Exception) {
            close()
            throw error
        }
    }

    /** Draws [bitmap] as the next frame of the video, presented [timeUs] microseconds into it. */
    fun draw(bitmap: Bitmap, timeUs: Long) = withOwnContext {
        GLES20.glViewport(0, 0, side, side)
        GLES20.glUseProgram(program)
        GLES20.glActiveTexture(GLES20.GL_TEXTURE0)
        GLES20.glBindTexture(GLES20.GL_TEXTURE_2D, texture)
        GLUtils.texImage2D(GLES20.GL_TEXTURE_2D, 0, bitmap, 0)

        val stride = 4 * Float.SIZE_BYTES
        quad.position(0)
        GLES20.glVertexAttribPointer(positionAttribute, 2, GLES20.GL_FLOAT, false, stride, quad)
        quad.position(2)
        GLES20.glVertexAttribPointer(uvAttribute, 2, GLES20.GL_FLOAT, false, stride, quad)
        GLES20.glEnableVertexAttribArray(positionAttribute)
        GLES20.glEnableVertexAttribArray(uvAttribute)
        GLES20.glDrawArrays(GLES20.GL_TRIANGLE_STRIP, 0, 4)
        check(GLES20.glGetError() == GLES20.GL_NO_ERROR) { "Bild konnte nicht gezeichnet werden" }

        // In nanoseconds here; the encoder hands it back in microseconds on the frame it writes.
        check(EGLExt.eglPresentationTimeANDROID(display, window, timeUs * 1000)) { "Zeitstempel abgewiesen" }
        check(EGL14.eglSwapBuffers(display, window)) { "Videoframe abgewiesen" }
    }

    override fun close() {
        if (display == EGL14.EGL_NO_DISPLAY) return
        if (context != EGL14.EGL_NO_CONTEXT && window != EGL14.EGL_NO_SURFACE) {
            runCatching {
                withOwnContext {
                    if (texture != 0) GLES20.glDeleteTextures(1, intArrayOf(texture), 0)
                    if (program != 0) GLES20.glDeleteProgram(program)
                }
            }
        }
        if (window != EGL14.EGL_NO_SURFACE) EGL14.eglDestroySurface(display, window)
        if (context != EGL14.EGL_NO_CONTEXT) EGL14.eglDestroyContext(display, context)
        // Deliberately no eglReleaseThread and no eglTerminate: the thread is the recogniser's and the display is the
        // process's default one, so either would reach contexts that are not ours.
        display = EGL14.EGL_NO_DISPLAY
        window = EGL14.EGL_NO_SURFACE
        context = EGL14.EGL_NO_CONTEXT
    }

    /**
     * Runs [block] with this surface's context current, and afterwards restores the context, display and surfaces
     * that were current on the thread before - or none, when none was.
     */
    private fun <T> withOwnContext(block: () -> T): T {
        val previousDisplay = EGL14.eglGetCurrentDisplay()
        val previousContext = EGL14.eglGetCurrentContext()
        val previousDraw = EGL14.eglGetCurrentSurface(EGL14.EGL_DRAW)
        val previousRead = EGL14.eglGetCurrentSurface(EGL14.EGL_READ)
        check(EGL14.eglMakeCurrent(display, window, window, context)) { "EGL-Kontext nicht verfügbar" }
        try {
            return block()
        } finally {
            if (previousContext == EGL14.EGL_NO_CONTEXT) {
                EGL14.eglMakeCurrent(display, EGL14.EGL_NO_SURFACE, EGL14.EGL_NO_SURFACE, EGL14.EGL_NO_CONTEXT)
            } else {
                EGL14.eglMakeCurrent(previousDisplay, previousDraw, previousRead, previousContext)
            }
        }
    }

    private fun chooseConfig(): EGLConfig {
        val attributes = intArrayOf(
            EGL14.EGL_RED_SIZE, 8,
            EGL14.EGL_GREEN_SIZE, 8,
            EGL14.EGL_BLUE_SIZE, 8,
            EGL14.EGL_RENDERABLE_TYPE, EGL14.EGL_OPENGL_ES2_BIT,
            EGL14.EGL_SURFACE_TYPE, EGL14.EGL_WINDOW_BIT,
            // Marks the configuration as one a video encoder can take; some encoders refuse a surface without it.
            EGL_RECORDABLE_ANDROID, 1,
            EGL14.EGL_NONE,
        )
        val configs = arrayOfNulls<EGLConfig>(1)
        val count = IntArray(1)
        check(EGL14.eglChooseConfig(display, attributes, 0, configs, 0, 1, count, 0) && count[0] > 0) {
            "Keine EGL-Konfiguration für den Encoder"
        }
        return checkNotNull(configs[0])
    }

    private fun link(): Int {
        val vertex = compile(GLES20.GL_VERTEX_SHADER, VERTEX_SHADER)
        val fragment = compile(GLES20.GL_FRAGMENT_SHADER, FRAGMENT_SHADER)
        val linked = GLES20.glCreateProgram()
        GLES20.glAttachShader(linked, vertex)
        GLES20.glAttachShader(linked, fragment)
        GLES20.glLinkProgram(linked)
        GLES20.glDeleteShader(vertex)
        GLES20.glDeleteShader(fragment)
        val status = IntArray(1)
        GLES20.glGetProgramiv(linked, GLES20.GL_LINK_STATUS, status, 0)
        check(status[0] != 0) { GLES20.glGetProgramInfoLog(linked) }
        GLES20.glUseProgram(linked)
        GLES20.glUniform1i(GLES20.glGetUniformLocation(linked, "image"), 0)
        return linked
    }

    private fun compile(type: Int, source: String): Int {
        val shader = GLES20.glCreateShader(type)
        GLES20.glShaderSource(shader, source)
        GLES20.glCompileShader(shader)
        val status = IntArray(1)
        GLES20.glGetShaderiv(shader, GLES20.GL_COMPILE_STATUS, status, 0)
        if (status[0] == 0) {
            val message = GLES20.glGetShaderInfoLog(shader)
            GLES20.glDeleteShader(shader)
            error(message)
        }
        return shader
    }

    private fun createTexture(): Int {
        val names = IntArray(1)
        GLES20.glGenTextures(1, names, 0)
        GLES20.glBindTexture(GLES20.GL_TEXTURE_2D, names[0])
        GLES20.glTexParameteri(GLES20.GL_TEXTURE_2D, GLES20.GL_TEXTURE_MIN_FILTER, GLES20.GL_LINEAR)
        GLES20.glTexParameteri(GLES20.GL_TEXTURE_2D, GLES20.GL_TEXTURE_MAG_FILTER, GLES20.GL_LINEAR)
        GLES20.glTexParameteri(GLES20.GL_TEXTURE_2D, GLES20.GL_TEXTURE_WRAP_S, GLES20.GL_CLAMP_TO_EDGE)
        GLES20.glTexParameteri(GLES20.GL_TEXTURE_2D, GLES20.GL_TEXTURE_WRAP_T, GLES20.GL_CLAMP_TO_EDGE)
        return names[0]
    }

    private companion object {
        /** `EGL_RECORDABLE_ANDROID`, which EGL14 has no constant for. */
        const val EGL_RECORDABLE_ANDROID = 0x3142

        const val VERTEX_SHADER = """
            attribute vec2 position;
            attribute vec2 uv;
            varying vec2 tex;
            void main() { gl_Position = vec4(position, 0.0, 1.0); tex = uv; }
        """

        const val FRAGMENT_SHADER = """
            precision mediump float;
            varying vec2 tex;
            uniform sampler2D image;
            void main() { gl_FragColor = texture2D(image, tex); }
        """
    }
}
