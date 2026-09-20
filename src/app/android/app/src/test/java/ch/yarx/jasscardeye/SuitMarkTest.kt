package ch.yarx.jasscardeye

import org.junit.Assert.assertEquals
import org.junit.Test

/** Every suit has a drawn mark of its own - none quietly wears another suit's. */
class SuitMarkTest {

    @Test
    fun everySuitHasItsOwnMark() {
        val marks = JassSuit.all.map { suitDrawable(it) }
        assertEquals(JassSuit.all.size, marks.toSet().size)
    }
}
