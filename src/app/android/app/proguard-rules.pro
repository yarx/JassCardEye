# No rules of our own for LiteRT. Its AARs bring consumer rules that keep what its native code reaches
# by name (everything annotated @UsedByReflection), and the default rules keep the names of every class
# with native methods. A blanket -keep of org.tensorflow.lite.** would keep the whole library, used or not,
# and a -dontwarn would hide a class R8 cannot find - the very error that should stop a release.
