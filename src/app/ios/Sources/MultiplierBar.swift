import SwiftUI

/// The factor the written result is multiplied by, as one row of buttons.
///
/// Asked in the start questions, next to the discipline, and ×1 unless somebody picks another. It is
/// not a property of the discipline - the same Obenabe counts triple at one table and single at the
/// next, and in a Coiffeur it depends on which box is still free - but it is known before the first
/// card is laid down, and settling it there keeps the counting screen to the pile and the score.
///
/// One row, no picker: all eight factors in reach with one tap, like the disciplines above it.
struct MultiplierBar: View {
    @Binding var multiplier: Int

    var body: some View {
        HStack(spacing: 4) {
            ForEach(JassRules.multipliers, id: \.self) { factor in
                Button {
                    multiplier = factor
                } label: {
                    Text("×\(factor)")
                        .font(.callout.weight(factor == multiplier ? .bold : .regular)
                            .monospacedDigit())
                        .frame(maxWidth: .infinity)
                        .padding(.vertical, 8)
                        .background(factor == multiplier ? Color.green : Color.white.opacity(0.14),
                                    in: RoundedRectangle(cornerRadius: 8))
                        .foregroundStyle(factor == multiplier ? Color.black : Color.white)
                        .contentShape(RoundedRectangle(cornerRadius: 8))
                }
                .buttonStyle(.plain)
                .accessibilityLabel(String(localized: "start.factor_value", defaultValue: "Faktor \(factor)"))
                .accessibilityAddTraits(factor == multiplier ? [.isSelected] : [])
            }
        }
    }
}
